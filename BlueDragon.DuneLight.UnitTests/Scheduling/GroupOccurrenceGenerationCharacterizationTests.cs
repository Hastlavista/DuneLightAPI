#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix H): Group → GroupSlot → generated occurrence → Appointment → Bookings, through GroupService
/// (AddMember / GenerateAppointments). The occurrence is a row of the SAME <c>appointments</c> table as an individual
/// appointment (Form = Group, GroupId + GroupSlotId set). Trainer, room, service and duration are COPIED from the Group
/// at generation time; from then on the occurrence is independent of the Group.
///
/// The fixture group is a 60-minute, price-15 Group-mode service with a weekly slot at 10:00 on the weekday of
/// <see cref="SchedulingWorld.FutureDay"/>, so generating exactly that day yields exactly one occurrence.
/// </summary>
public class GroupOccurrenceGenerationCharacterizationTests
{
    private static async Task<(SchedulingWorld World, ServiceEntity GroupService)> Arrange(string name)
    {
        SchedulingWorld w = await SchedulingWorld.Create(name);
        ServiceEntity groupService = await w.AddGroupService();
        return (w, groupService);
    }

    #region Shape of the generated occurrence

    [Fact]
    public async Task Generate_CreatesAGroupFormAppointmentInTheSameTable_WithGroupAndSlotLinks()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_CreatesAGroupFormAppointmentInTheSameTable_WithGroupAndSlotLinks));
        await using SchedulingWorld _ = w;
        Room room = await w.AddRoom();
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Appointment a = await w.LoadAppointment(Assert.Single(result.Created).Id);
        Assert.Equal(AppointmentForm.Group, a.Form);
        Assert.Equal(group.Id, a.GroupId);
        Assert.Equal(group.Slots.Single().Id, a.GroupSlotId);
        Assert.Equal(AppointmentStatus.Scheduled, a.Status);
        Assert.Equal(SchedulingWorld.Future(10), a.StartsAt);
        Assert.Equal(w.OrganizationId, a.OrganizationId);
        Assert.Equal(w.Company.Id, a.CompanyId);
        Assert.Equal(w.ActorUserId, a.CreatedBy);
        Assert.Null(a.RecurrenceGroupId);
        // Same table as individual appointments: it is countable through the one Appointments set.
        Assert.Equal(1, await w.CountAppointments(q => q.Where(x => x.Form == AppointmentForm.Group)));
    }

    [Fact]
    public async Task Generate_CopiesServiceTrainerRoomAndDurationFromTheGroup()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_CopiesServiceTrainerRoomAndDurationFromTheGroup));
        await using SchedulingWorld _ = w;
        Room room = await w.AddRoom();
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Equal(svc.Id, a.ServiceId);
        Assert.Equal(w.Employee.Id, a.EmployeeId); // Group.DefaultTrainerId
        Assert.Equal(room.Id, a.RoomId); // Group.DefaultRoomId
        Assert.Equal(60, a.DurationMinutes); // the Group has no duration of its own — it is the Service default
    }

    [Fact]
    public async Task Generate_ForAGroupWithoutATrainer_LeavesTheOccurrenceTrainerless()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ForAGroupWithoutATrainer_LeavesTheOccurrenceTrainerless));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5, withTrainer: false);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Null(a.EmployeeId);
    }

    [Fact]
    public async Task Generate_ForAGroupWithoutARoom_LeavesTheOccurrenceRoomless()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ForAGroupWithoutARoom_LeavesTheOccurrenceRoomless));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Null(a.RoomId);
    }

    #endregion

    #region Bookings

    [Fact]
    public async Task Generate_CreatesOneConfirmedBookingPerActiveMember_WithTheResolvedPriceSnapshot()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_CreatesOneConfirmedBookingPerActiveMember_WithTheResolvedPriceSnapshot));
        await using SchedulingWorld _ = w;
        Client second = await w.AddClient("Second", "Member");
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, second);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Equal(2, a.Bookings.Count);
        Assert.All(a.Bookings, b =>
        {
            Assert.Equal(BookingStatus.Confirmed, b.Status);
            Assert.Equal(0, b.StatusVersion);
            Assert.Equal(15m, b.Amount);
            Assert.Equal(15m, b.SuggestedAmount);
            Assert.False(b.IsAmountManuallyOverridden);
            Assert.Null(b.CoverageType);
            Assert.Null(b.ClientPackageId);
            Assert.False(b.PackageCoverageApplied);
        });
        Assert.Equal(
            new[] { w.Client.Id, second.Id }.OrderBy(x => x).ToArray(),
            a.Bookings.Select(b => (Guid?)b.ClientId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Generate_ResolvesTheBookingPriceForTheOccurrenceDateAndCompany()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ResolvesTheBookingPriceForTheOccurrenceDateAndCompany));
        await using SchedulingWorld _ = w;
        await w.AddPriceListItem(svc, 22m, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), companyId: w.Company.Id);
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Equal(22m, a.Bookings.Single().Amount);
        Assert.Equal(22m, a.Bookings.Single().SuggestedAmount);
    }

    [Fact]
    public async Task Generate_SkipsMembersWhoLeftTheGroup()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_SkipsMembersWhoLeftTheGroup));
        await using SchedulingWorld _ = w;
        Client leaver = await w.AddClient("Leaver", "Member");
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        GroupDto afterAdds = await w.AddGroupMember(group, w.Client);
        afterAdds = await w.AddGroupMember(group, leaver);
        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, group.Id);
        Guid leaverMemberId = detail.Members.Single(m => m.ClientId == leaver.Id).Id;
        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, group.Id, leaverMemberId);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Equal(w.Client.Id, Assert.Single(a.Bookings).ClientId);
    }

    [Fact]
    public async Task Generate_ForAGroupWithoutMembers_CreatesAnEmptyOccurrence()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ForAGroupWithoutMembers_CreatesAnEmptyOccurrence));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.Empty(a.Bookings);
    }

    [Fact]
    public async Task Generate_DoesNotAutoCreateBookingsForGuests_OnlyForMembers()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_DoesNotAutoCreateBookingsForGuests_OnlyForMembers));
        await using SchedulingWorld _ = w;
        Client stranger = await w.AddClient("Stranger", "Client");
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);

        Appointment a = await w.GenerateSingleOccurrence(group);

        Assert.DoesNotContain(a.Bookings, b => b.ClientId == stranger.Id);
    }

    #endregion

    #region Idempotence and batch shape

    [Fact]
    public async Task Generate_Twice_ForTheSameRange_IsIdempotent_SecondCallSkipsExistingOccurrences()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_Twice_ForTheSameRange_IsIdempotent_SecondCallSkipsExistingOccurrences));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        GenerateGroupAppointmentsResult second = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(1, second.SkippedCount);
        Assert.Equal(1, await w.CountAppointments(q => q.Where(a => a.Form == AppointmentForm.Group)));
    }

    [Fact]
    public async Task Generate_ForAWiderRange_OnlyAddsTheMissingOccurrences()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ForAWiderRange_OnlyAddsTheMissingOccurrences));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        GenerateGroupAppointmentsResult wider = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(14));

        Assert.Equal(2, wider.CreatedCount); // week 2 and week 3; week 1 already existed
        Assert.Equal(1, wider.SkippedCount);
        Assert.Equal(3, await w.CountAppointments(q => q.Where(a => a.Form == AppointmentForm.Group)));
    }

    [Fact]
    public async Task Generate_CreatesOneOccurrencePerActiveSlotPerMatchingDay()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_CreatesOneOccurrencePerActiveSlotPerMatchingDay));
        await using SchedulingWorld _ = w;
        DayOfWeek day = SchedulingWorld.FutureDay.DayOfWeek;
        GroupDto group = await w.CreateGroup(svc, capacity: 5, slots: new[] { (day, TimeSpan.FromHours(9)), (day, TimeSpan.FromHours(15)) });

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(
            new[] { SchedulingWorld.Future(9), SchedulingWorld.Future(15) },
            result.Created.Select(c => c.StartsAt).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Generate_ForAnInactiveGroup_CreatesNothing()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_ForAnInactiveGroup_CreatesNothing));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.Groups.SetActive(w.OrganizationId, w.ActorUserId, group.Id, false);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Generate_SkipsCompanyHolidaysSilently_WithoutFailingAndCountsThemAsSkipped()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_SkipsCompanyHolidaysSilently_WithoutFailingAndCountsThemAsSkipped));
        await using SchedulingWorld _ = w;
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);
        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);

        // Unlike Create/CreateRecurring for individual appointments (holiday = hard error), group generation skips the date.
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Generate_RejectsAnEndDateBeforeTheStartDate()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_RejectsAnEndDateBeforeTheStartDate));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);

        await SchedulingAssert.Validation(() => w.GenerateOccurrences(group, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(-1)));
    }

    [Fact]
    public async Task DuplicateOccurrence_IsAlsoStoppedAtWriteTime_UnderTheSlotLock()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(DuplicateOccurrence_IsAlsoStoppedAtWriteTime_UnderTheSlotLock));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Appointment existing = await w.GenerateSingleOccurrence(group);

        // The application dedupes first (previous tests). CHANGED in D3A: the safety net beneath it used to be the unique
        // index ux_appointments_group_slot_startsat on appointments(group_slot_id, starts_at); starts_at no longer exists
        // (the start lives on the segment), so the net is now GroupHandler.AddAppointments — a per-slot transaction lock
        // plus a re-check of existing (slot, start) pairs before inserting. A duplicate write is refused and nothing is saved.
        AppointmentSegment existingSegment = existing.Segments.Single();
        Appointment duplicate = AppointmentFactory.CreateGroupOccurrence(
            w.OrganizationId, existing.CompanyId, existing.GroupId.Value, existing.GroupSlotId.Value, w.ActorUserId, DateTimeOffset.UtcNow,
            new[]
            {
                new SegmentPlan(existingSegment.ServiceId, existingSegment.PlannedStart, existingSegment.PlannedEnd,
                    existingSegment.Employees.Select(e => e.EmployeeId).ToList(), existingSegment.RoomId, new List<ParticipantPlan>(),
                    GroupSegmentTemplateId: existingSegment.GroupSegmentTemplateId)
            });

        bool added = await w.Resolve<IGroupHandler>().AddAppointments(new List<Appointment> { duplicate });

        Assert.False(added);
        Assert.Equal(1, await w.CountAppointments(q => q.Where(a => a.GroupSlotId == existing.GroupSlotId)));
    }

    #endregion

    #region Conflict checks at generation (all-or-nothing batch)

    [Fact]
    public async Task Generate_WhenTheTrainerAlreadyHasAnAppointmentAtThatTime_FailsTheWholeBatch()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_WhenTheTrainerAlreadyHasAnAppointmentAtThatTime_FailsTheWholeBatch));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        // The trainer is also authorized for the default Service; an individual appointment at 10:00 blocks the 10:00 slot.
        await w.CreateAppointment(SchedulingWorld.Future(10));

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(group, SchedulingWorld.FutureDay));

        Assert.Contains(ErrorCodes.RecurringConflictReasonAppointment, SchedulingAssert.ConflictReasons(ex));
        Assert.Equal(0, await w.CountAppointments(q => q.Where(a => a.Form == AppointmentForm.Group)));
    }

    [Fact]
    public async Task Generate_WhenTheTrainerIsAbsent_FailsUnlessAvailabilityIsOverridden_ThenWarns()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_WhenTheTrainerIsAbsent_FailsUnlessAvailabilityIsOverridden_ThenWarns));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddAbsence(w.Employee, SchedulingWorld.FutureDay);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(group, SchedulingWorld.FutureDay));
        Assert.Contains(ErrorCodes.RecurringConflictReasonRosterAbsence, SchedulingAssert.ConflictReasons(ex));
        Assert.Equal(0, await w.CountAppointments());

        GenerateGroupAppointmentsResult overridden = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay, overrideAvailability: true);

        Assert.Equal(1, overridden.CreatedCount);
        Assert.Contains(Assert.Single(overridden.Created).Warnings, x => x.Code == WarningCodes.EmployeeAbsent);
    }

    [Fact]
    public async Task Generate_WhenTheRoomIsOccupied_FailsTheWholeBatch_AndOverrideDoesNotHelp()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_WhenTheRoomIsOccupied_FailsTheWholeBatch_AndOverrideDoesNotHelp));
        await using SchedulingWorld _ = w;
        Room room = await w.AddRoom();
        Employee otherEmployee = await w.AddEmployee("Other");
        Client otherClient = await w.AddClient("Other", "Client");
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: room);
        await w.CreateAppointment(SchedulingWorld.Future(10), client: otherClient, employee: otherEmployee, room: room);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(group, SchedulingWorld.FutureDay, overrideAvailability: true));

        Assert.Contains(ErrorCodes.RecurringConflictReasonRoom, SchedulingAssert.ConflictReasons(ex));
    }

    [Fact]
    public async Task Generate_WhenAnActiveMemberIsBusyElsewhere_FailsTheWholeBatch()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_WhenAnActiveMemberIsBusyElsewhere_FailsTheWholeBatch));
        await using SchedulingWorld _ = w;
        Employee otherEmployee = await w.AddEmployee("Other");
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        await w.CreateAppointment(SchedulingWorld.Future(10), client: w.Client, employee: otherEmployee);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(group, SchedulingWorld.FutureDay));

        Assert.Contains(ErrorCodes.RecurringConflictReasonMemberConflict, SchedulingAssert.ConflictReasons(ex));
    }

    [Fact]
    public async Task Generate_WhenTheGroupHasMoreActiveMembersThanItsCapacity_ReproducesTheMembership()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(Generate_WhenTheGroupHasMoreActiveMembersThanItsCapacity_ReproducesTheMembership));
        await using SchedulingWorld _ = w;
        Client second = await w.AddClient("Second", "Member");
        GroupDto group = await w.CreateGroup(svc, capacity: 2);
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, second);
        // Lowering the capacity below the current roster is allowed by the template edit.
        await w.UpdateOnlyTemplate(group, r => r.Capacity = 1);

        // CHANGED in M1F: capacity is a SOFT business limit of the segment template. Generation reproduces the existing
        // membership (validly created earlier) instead of refusing — nobody is dropped; only NEW additions above the limit
        // need an explicit override. Hard physical rules still apply at generation.
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        Assert.Equal(2, occurrence.Bookings.Count);
    }

    #endregion

    #region Independence from later Group edits

    [Fact]
    public async Task GeneratedOccurrence_KeepsItsSnapshot_WhenTheGroupTrainerRoomAndCapacityAreEditedLater()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(GeneratedOccurrence_KeepsItsSnapshot_WhenTheGroupTrainerRoomAndCapacityAreEditedLater));
        await using SchedulingWorld _ = w;
        Room originalRoom = await w.AddRoom();
        Room newRoom = await w.AddRoom();
        Employee newTrainer = await w.AddEmployee("New trainer");
        await w.AssignEmployeeToService(newTrainer, svc);
        GroupDto group = await w.CreateGroup(svc, capacity: 5, room: originalRoom);
        await w.AddGroupMember(group, w.Client);
        Appointment before = await w.GenerateSingleOccurrence(group);

        await w.Groups.Update(w.OrganizationId, w.ActorUserId, group.Id,
            new GroupUpdateRequest { Name = "renamed", CompanyId = group.CompanyId });
        await w.UpdateOnlyTemplate(group, r =>
        {
            r.Capacity = 9;
            r.EmployeeIds = new List<Guid> { newTrainer.Id.Value };
            r.RoomId = newRoom.Id;
        });

        Appointment after = await w.LoadAppointment(before.Id.Value);
        Assert.Equal(w.Employee.Id, after.EmployeeId);
        Assert.Equal(originalRoom.Id, after.RoomId);
        Assert.Equal(before.ServiceId, after.ServiceId);
        Assert.Equal(before.DurationMinutes, after.DurationMinutes);
        Assert.Equal(before.StartsAt, after.StartsAt);
        Assert.Single(after.Bookings);
    }

    [Fact]
    public async Task GeneratedOccurrence_KeepsItsDuration_WhenTheLiveServiceDurationChangesLater()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(GeneratedOccurrence_KeepsItsDuration_WhenTheLiveServiceDurationChangesLater));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Appointment before = await w.GenerateSingleOccurrence(group);
        await w.UpdateService(svc, durationMinutes: 90);

        Assert.Equal(60, (await w.LoadAppointment(before.Id.Value)).DurationMinutes);
    }

    [Fact]
    public async Task GeneratedOccurrence_SurvivesGroupDeactivationAndSlotRemoval()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(GeneratedOccurrence_SurvivesGroupDeactivationAndSlotRemoval));
        await using SchedulingWorld _ = w;
        GroupDto group = await w.CreateGroup(svc, capacity: 5, slots: new[]
        {
            (SchedulingWorld.FutureDay.DayOfWeek, TimeSpan.FromHours(10)),
            (SchedulingWorld.FutureDay.DayOfWeek, TimeSpan.FromHours(15))
        });
        GenerateGroupAppointmentsResult generated = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay);
        Assert.Equal(2, generated.CreatedCount);
        Guid slotToRemove = group.Slots.First().Id;

        await w.Groups.RemoveSlot(w.OrganizationId, w.ActorUserId, group.Id, slotToRemove);
        await w.Groups.SetActive(w.OrganizationId, w.ActorUserId, group.Id, false);

        // The docs claim deactivation deletes future occurrences; the code does not (only stops FUTURE generation).
        Assert.Equal(2, await w.CountAppointments(q => q.Where(a => a.Form == AppointmentForm.Group && a.Status == AppointmentStatus.Scheduled)));
    }

    #endregion

    #region Group creation guards

    [Fact]
    public async Task CreateGroup_WithAnIndividualModeService_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CreateGroup_WithAnIndividualModeService_IsRejected));

        await SchedulingAssert.BusinessRule(ErrorCodes.ServiceNotGroupMode, () => w.CreateGroup(w.Service, capacity: 5));
    }

    [Fact]
    public async Task CreateGroup_WithoutSlots_IsRejected()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(CreateGroup_WithoutSlots_IsRejected));
        await using SchedulingWorld _ = w;

        await SchedulingAssert.Validation(() => w.Groups.Create(w.OrganizationId, w.ActorUserId, new GroupCreateRequest
        {
            Name = "no slots", CompanyId = w.Company.Id.Value,
            SegmentTemplates = new List<GroupSegmentTemplateRequest> { new() { ServiceId = svc.Id.Value, Capacity = 5 } }
        }));
    }

    [Fact]
    public async Task CreateGroup_WithATrainerNotAuthorizedForTheService_IsRejected()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(CreateGroup_WithATrainerNotAuthorizedForTheService_IsRejected));
        await using SchedulingWorld _ = w;
        Employee unauthorized = await w.AddEmployeeRestrictedToAnotherService();

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService, () => w.CreateGroup(svc, capacity: 5, trainer: unauthorized));
    }

    [Fact]
    public async Task TwoGroupsInTheSameRoomAtTheSameWeeklySlot_AreAllowed_TheRoomsPeopleCapacityDecidesAtSchedulingTime()
    {
        (SchedulingWorld w, ServiceEntity svc) = await Arrange(nameof(TwoGroupsInTheSameRoomAtTheSameWeeklySlot_AreAllowed_TheRoomsPeopleCapacityDecidesAtSchedulingTime));
        await using SchedulingWorld _ = w;
        Room room = await w.AddRoom(capacity: 3);
        Employee secondTrainer = await w.AddEmployee("Second trainer", serviceId: svc.Id);
        GroupDto first = await w.CreateGroup(svc, capacity: 5, room: room);
        await w.AddGroupMember(first, w.Client);

        // CHANGED in M1D: the legacy "exclusive room per weekly slot" setup rule is gone — capacity is checked on real segments.
        GroupDto second = await w.CreateGroup(svc, capacity: 5, trainer: secondTrainer, room: room);

        await w.GenerateSingleOccurrence(first);   // trainer + member = 2 people
        Appointment parallel = await w.GenerateSingleOccurrence(second); // + its trainer = 3 people: fits exactly
        Client late = await w.AddClient("Late", "Member");

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => w.AddGroupMember(second, late));
        Assert.Empty((await w.LoadAppointment(parallel.Id.Value)).Bookings);
    }

    #endregion
}
