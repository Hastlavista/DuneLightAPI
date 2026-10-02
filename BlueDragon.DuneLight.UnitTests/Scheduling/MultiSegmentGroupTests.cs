using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1F — multi-template Groups with selective participation: one occurrence Appointment with one Segment per
/// GroupSegmentTemplate, one Booking per client, Participations only on selected templates; segment-specific soft capacity
/// with an explicit, grant-checked override (hard Room/Resource/Employee/Client rules stay hard); member selection changes;
/// RemoveMember lateness per Participation; segment-specific waitlist and promotion; segment-addressed guests; close-out over
/// all segments.
/// </summary>
public class MultiSegmentGroupTests
{
    /// <summary>"Wellness Morning": slot 09:00 on FutureDay's weekday; A Yoga (0, 60), B Massage (+60, 30), C Recovery (+120, 45).</summary>
    private sealed record Wellness(GroupDto Group, Guid A, Guid B, Guid C, ServiceEntity Yoga, ServiceEntity Massage, ServiceEntity Recovery);

    private static async Task<Wellness> CreateWellness(
        SchedulingWorld w, int capA = 15, int capB = 4, int capC = 8, bool withTrainer = true, Room roomA = null, TimeSpan? slot = null)
    {
        ServiceEntity yoga = await w.AddGroupService(60, 15m);
        ServiceEntity massage = await w.AddGroupService(30, 40m);
        ServiceEntity recovery = await w.AddGroupService(45, 20m);
        GroupDto group = await w.Groups.Create(w.OrganizationId, w.ActorUserId, new GroupCreateRequest
        {
            Name = $"Wellness-{Guid.NewGuid():N}",
            CompanyId = w.Company.Id.Value,
            DefaultTrainerId = withTrainer ? w.Employee.Id : null,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = SchedulingWorld.FutureDay.DayOfWeek, StartTime = slot ?? TimeSpan.FromHours(9) } },
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new() { ServiceId = yoga.Id.Value, StartOffsetMinutes = 0, Capacity = capA, RoomId = roomA?.Id },
                new() { ServiceId = massage.Id.Value, StartOffsetMinutes = 60, DurationMinutes = 30, Capacity = capB },
                new() { ServiceId = recovery.Id.Value, StartOffsetMinutes = 120, DurationMinutes = 45, Capacity = capC }
            }
        });
        Guid Of(ServiceEntity s) => group.SegmentTemplates.Single(t => t.ServiceId == s.Id).Id;
        return new Wellness(group, Of(yoga), Of(massage), Of(recovery), yoga, massage, recovery);
    }

    private static Task<GroupDto> Join(SchedulingWorld w, GroupDto group, Client client, bool overrideCapacity = false, Guid? userId = null,
        params Guid[] templates) =>
        w.Groups.AddMember(w.OrganizationId, userId ?? w.ActorUserId, group.Id, new GroupMemberAddRequest
        {
            ClientId = client.Id.Value,
            SegmentTemplateIds = templates.Length == 0 ? null : templates.ToList(),
            OverrideCapacity = overrideCapacity
        });

    private static Task<GroupDto> Join(SchedulingWorld w, GroupDto group, Client client, params Guid[] templates) =>
        Join(w, group, client, false, null, templates);

    private static AppointmentSegment SegmentOf(Appointment occurrence, Guid templateId) =>
        occurrence.Segments.Single(s => s.GroupSegmentTemplateId == templateId);

    private static Booking BookingOf(Appointment occurrence, Client client) => occurrence.Bookings.Single(b => b.ClientId == client.Id);

    private static Guid MemberId(GroupDetailDto detail, Client client) => detail.Members.Single(m => m.ClientId == client.Id).Id;

    #region Selective participation and generation

    [Fact]
    public async Task Generation_IsOneAppointment_OneSegmentPerTemplate_OneBookingPerClient_ParticipationsOnlyOnSelectedTemplates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Generation_IsOneAppointment_OneSegmentPerTemplate_OneBookingPerClient_ParticipationsOnlyOnSelectedTemplates));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        Client ivana = await w.AddClient("Ivana");
        await Join(w, g.Group, ana, g.A, g.C);
        await Join(w, g.Group, marko, g.A, g.B);
        await Join(w, g.Group, ivana, g.B);

        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);

        Assert.Equal(1, await w.CountAppointments());
        Assert.Equal(3, occurrence.Segments.Count);
        Assert.Equal(3, occurrence.Bookings.Count);
        Assert.Equal(new[] { g.A, g.C }.OrderBy(x => x), BookingOf(occurrence, ana).Participations
            .Select(p => occurrence.Segments.Single(s => s.Id == p.AppointmentSegmentId).GroupSegmentTemplateId.Value).OrderBy(x => x));
        Assert.Equal(new[] { g.A, g.B }.OrderBy(x => x), BookingOf(occurrence, marko).Participations
            .Select(p => occurrence.Segments.Single(s => s.Id == p.AppointmentSegmentId).GroupSegmentTemplateId.Value).OrderBy(x => x));
        Assert.Equal(SegmentOf(occurrence, g.B).Id, Assert.Single(BookingOf(occurrence, ivana).Participations).AppointmentSegmentId);
        // Segment A: Ana + Marko; B: Marko + Ivana; C: Ana only.
        int On(Guid template) => occurrence.Bookings.SelectMany(b => b.Participations).Count(p => p.AppointmentSegmentId == SegmentOf(occurrence, template).Id);
        Assert.Equal(new[] { 2, 2, 1 }, new[] { On(g.A), On(g.B), On(g.C) });
        // Price per participation: the template's service.
        Assert.Equal(40m, BookingOf(occurrence, ivana).Participations.Single().Amount);
        // The single trainer staffs every generated segment (at most one employee per segment).
        Assert.All(occurrence.Segments, s => Assert.Equal(w.Employee.Id, Assert.Single(s.Employees).EmployeeId));
    }

    [Fact]
    public async Task OneBookingPerClient_AllThreeTemplates_ThreeParticipations_AndALaterSelectionReusesTheBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OneBookingPerClient_AllThreeTemplates_ThreeParticipations_AndALaterSelectionReusesTheBooking));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        await Join(w, g.Group, ana, g.A, g.B, g.C);
        await Join(w, g.Group, marko, g.A);

        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        Booking anaBooking = BookingOf(occurrence, ana);
        Assert.Equal(3, anaBooking.Participations.Count);
        Guid markoBooking = BookingOf(occurrence, marko).Id.Value;

        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, g.Group.Id);
        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, MemberId(detail, marko),
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.A, g.B } });

        Appointment after = await w.LoadAppointment(occurrence.Id.Value);
        Booking reused = BookingOf(after, marko);
        Assert.Equal(markoBooking, reused.Id);
        Assert.Equal(2, reused.Participations.Count);
        Assert.Equal(2, after.Bookings.Count);
        Assert.Equal(new[] { g.A, g.B }.OrderBy(x => x),
            (await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members.Single(m => m.ClientId == marko.Id).SegmentTemplateIds);
    }

    [Fact]
    public async Task TemplateTiming_IsRelativeToTheSlot_WithGaps_AndTheRangeSpansAllSegments()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TemplateTiming_IsRelativeToTheSlot_WithGaps_AndTheRangeSpansAllSegments));
        Wellness g = await CreateWellness(w);

        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);

        Assert.Equal((SchedulingWorld.Future(9), SchedulingWorld.Future(10)), (SegmentOf(occurrence, g.A).PlannedStart, SegmentOf(occurrence, g.A).PlannedEnd));
        Assert.Equal((SchedulingWorld.Future(10), SchedulingWorld.Future(10, 30)), (SegmentOf(occurrence, g.B).PlannedStart, SegmentOf(occurrence, g.B).PlannedEnd));
        Assert.Equal((SchedulingWorld.Future(11), SchedulingWorld.Future(11, 45)), (SegmentOf(occurrence, g.C).PlannedStart, SegmentOf(occurrence, g.C).PlannedEnd));
        AppointmentDto dto = await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value);
        Assert.Equal(SchedulingWorld.Future(9), dto.PlannedStart);
        Assert.Equal(SchedulingWorld.Future(11, 45), dto.PlannedEnd);
        // A template with no explicit duration took the service default (60) as a proposal and owns it.
        Assert.Equal(60, g.Group.SegmentTemplates.Single(t => t.Id == g.A).DurationMinutes);
    }

    [Fact]
    public async Task TemplateTiming_FollowsTheWallClockAcrossDst_AndTheServiceDefaultIsOnlyAProposal()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TemplateTiming_FollowsTheWallClockAcrossDst_AndTheServiceDefaultIsOnlyAProposal), "Europe/Zagreb");
        Wellness g = await CreateWellness(w);
        // Changing the service default AFTER the template was created does not change the template's duration.
        await w.UpdateService(g.Yoga, durationMinutes: 90);

        // 2031-03-24 (CET, UTC+1) and 2031-03-31 (CEST, UTC+2) — both Mondays around the 2031-03-30 DST switch.
        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(g.Group,
            new DateTimeOffset(2031, 3, 24, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2031, 3, 31, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(2, result.CreatedCount);

        foreach ((DateTimeOffset day, int utcOffsetHours) in new[] { (new DateTimeOffset(2031, 3, 24, 0, 0, 0, TimeSpan.Zero), 1), (new DateTimeOffset(2031, 3, 31, 0, 0, 0, TimeSpan.Zero), 2) })
        {
            Appointment occurrence = await w.LoadAppointment(result.Created.Single(c => c.StartsAt.Date == day.Date).Id);
            // Local 09:00 / 10:00 / 11:00 on both sides of the switch.
            Assert.Equal(day.AddHours(9 - utcOffsetHours), SegmentOf(occurrence, g.A).PlannedStart);
            Assert.Equal(day.AddHours(10 - utcOffsetHours), SegmentOf(occurrence, g.A).PlannedEnd); // 60 (template), not 90
            Assert.Equal(day.AddHours(10 - utcOffsetHours), SegmentOf(occurrence, g.B).PlannedStart);
            Assert.Equal(day.AddHours(11 - utcOffsetHours), SegmentOf(occurrence, g.C).PlannedStart);
        }
    }

    [Fact]
    public async Task SelectingTwoOverlappingTemplates_IsRejectedByTheClientHardOverlap()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SelectingTwoOverlappingTemplates_IsRejectedByTheClientHardOverlap));
        Wellness g = await CreateWellness(w, withTrainer: false);
        GroupDto withOverlap = await w.Groups.AddSegmentTemplate(w.OrganizationId, w.ActorUserId, g.Group.Id,
            new GroupSegmentTemplateRequest { ServiceId = g.Massage.Id.Value, StartOffsetMinutes = 30, DurationMinutes = 60, Capacity = 5 });
        Guid d = withOverlap.SegmentTemplates.Single(t => t.StartOffsetMinutes == 30).Id;
        Client ana = await w.AddClient("Ana");

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Join(w, g.Group, ana, g.A, d));

        Assert.Empty((await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members);
    }

    [Fact]
    public async Task OverlappingTemplates_DifferentClients_AreAllowed_ButTheInheritedTrainerIsNeverDoubleBooked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OverlappingTemplates_DifferentClients_AreAllowed_ButTheInheritedTrainerIsNeverDoubleBooked));
        ServiceEntity yoga = await w.AddGroupService(60);
        ServiceEntity pilates = await w.AddGroupService(60);
        GroupCreateRequest Request(bool withTrainer) => new()
        {
            Name = $"Parallel-{Guid.NewGuid():N}",
            CompanyId = w.Company.Id.Value,
            DefaultTrainerId = withTrainer ? w.Employee.Id : null,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = SchedulingWorld.FutureDay.DayOfWeek, StartTime = TimeSpan.FromHours(9) } },
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new() { ServiceId = yoga.Id.Value, StartOffsetMinutes = 0, Capacity = 5 },
                new() { ServiceId = pilates.Id.Value, StartOffsetMinutes = 30, Capacity = 5 }
            }
        };

        GroupDto trainerless = await w.Groups.Create(w.OrganizationId, w.ActorUserId, Request(withTrainer: false));
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        await Join(w, trainerless, ana, trainerless.SegmentTemplates[0].Id);
        await Join(w, trainerless, marko, trainerless.SegmentTemplates[1].Id);
        Appointment parallel = await w.GenerateSingleOccurrence(trainerless);
        Assert.Equal(2, parallel.Segments.Count);

        GroupDto trained = await w.Groups.Create(w.OrganizationId, w.ActorUserId, Request(withTrainer: true));
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(trained, SchedulingWorld.FutureDay));
        Assert.Contains(ErrorCodes.RecurringConflictReasonAppointment, SchedulingAssert.ConflictReasons(ex));
        Assert.Equal(1, await w.CountAppointments());
    }

    #endregion

    #region Soft capacity, override and hard capacity

    [Fact]
    public async Task SoftCapacity_TheThirdNormalAdd_IsRejected_TheOverrideNeedsTheExplicitFlagAndTheGrant()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SoftCapacity_TheThirdNormalAdd_IsRejected_TheOverrideNeedsTheExplicitFlagAndTheGrant));
        Wellness g = await CreateWellness(w, capA: 2);
        Client c1 = await w.AddClient("One");
        Client c2 = await w.AddClient("Two");
        Client c3 = await w.AddClient("Three");
        Guid overrider = await w.AddMemberUser();
        await w.GrantUser(overrider, Grants.GroupsCapacityOverride);
        await Join(w, g.Group, c1, g.A);
        await Join(w, g.Group, c2, g.A);

        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => Join(w, g.Group, c3, g.A));
        // The grant alone never ignores capacity — the request must ask for it.
        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => Join(w, g.Group, c3, false, overrider, g.A));
        // Asking without the grant is an authorization failure.
        await Assert.ThrowsAsync<ForbiddenAppException>(() => Join(w, g.Group, c3, true, w.ActorUserId, g.A));

        GroupDto dto = await Join(w, g.Group, c3, true, overrider, g.A);
        Assert.Equal(3, dto.ActiveMemberCount);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        Assert.Equal(3, occurrence.Bookings.Count); // generation reproduces the (overridden) membership
    }

    [Fact]
    public async Task SoftCapacity_IsPerTemplate_NotOneGroupWideCount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SoftCapacity_IsPerTemplate_NotOneGroupWideCount));
        Wellness g = await CreateWellness(w, capA: 10, capB: 3);
        List<Client> clients = new();
        for (int i = 0; i < 5; i++)
            clients.Add(await w.AddClient($"C{i}"));
        for (int i = 0; i < 3; i++)
            await Join(w, g.Group, clients[i], g.A, g.B);

        // B is full (3), A has room (3 of 10).
        await SchedulingAssert.BusinessRule(ErrorCodes.GroupCapacityReached, () => Join(w, g.Group, clients[3], g.A, g.B));
        await Join(w, g.Group, clients[3], g.A);
        await Join(w, g.Group, clients[4], g.A);

        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, g.Group.Id);
        Assert.Equal(5, detail.Members.Count(m => m.SegmentTemplateIds.Contains(g.A)));
        Assert.Equal(3, detail.Members.Count(m => m.SegmentTemplateIds.Contains(g.B)));
    }

    [Fact]
    public async Task HardRoomCapacity_IsNeverOverridden_ByTheGroupCapacityOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(HardRoomCapacity_IsNeverOverridden_ByTheGroupCapacityOverride));
        Room room = await w.AddRoom(capacity: 3); // trainer + 2 people
        Wellness g = await CreateWellness(w, capA: 2, roomA: room);
        Client c1 = await w.AddClient("One");
        Client c2 = await w.AddClient("Two");
        Client c3 = await w.AddClient("Three");
        Guid overrider = await w.AddMemberUser();
        await w.GrantUser(overrider, Grants.GroupsCapacityOverride);
        await Join(w, g.Group, c1, g.A);
        await Join(w, g.Group, c2, g.A);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => Join(w, g.Group, c3, true, overrider, g.A));
        // A guest with the override is refused by the room too.
        Client guest = await w.AddClient("Guest");
        await w.GrantUser(w.ActorUserId, Grants.GroupsCapacityOverride);
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => w.Bookings.AddBooking(w.OrganizationId, w.ActorUserId, true,
            occurrence.Id.Value, new BookingCreateRequest { ClientId = guest.Id.Value, SegmentId = SegmentOf(occurrence, g.A).Id, OverrideCapacity = true }));

        Assert.Equal(2, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Count);
        Assert.Equal(2, (await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members.Count);
    }

    [Fact]
    public async Task HardResourceCapacity_BlocksGeneration()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(HardResourceCapacity_BlocksGeneration));
        Resource mats = await w.AddResource(capacity: 3, name: "Mats");
        ServiceEntity yoga = await w.AddGroupService(60);
        GroupCreateRequest Request() => new()
        {
            Name = $"Mats-{Guid.NewGuid():N}",
            CompanyId = w.Company.Id.Value,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = SchedulingWorld.FutureDay.DayOfWeek, StartTime = TimeSpan.FromHours(9) } },
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new() { ServiceId = yoga.Id.Value, StartOffsetMinutes = 0, Capacity = 5,
                    Resources = new List<GroupSegmentTemplateResourceRequest> { new() { ResourceId = mats.Id.Value, QuantityRequired = 2 } } }
            }
        };
        GroupDto first = await w.Groups.Create(w.OrganizationId, w.ActorUserId, Request());
        GroupDto second = await w.Groups.Create(w.OrganizationId, w.ActorUserId, Request());
        Appointment occurrence = await w.GenerateSingleOccurrence(first);
        await using (DatabaseContext db = w.NewDb())
        {
            AppointmentSegmentResource copied = await db.AppointmentSegmentResources.SingleAsync(r => r.AppointmentSegmentId == occurrence.Segments.Single().Id);
            Assert.Equal((mats.Id.Value, 2), (copied.ResourceId, copied.QuantityRequired)); // the template's resources are copied
        }

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict,
            () => w.GenerateOccurrences(second, SchedulingWorld.FutureDay));
        Assert.Contains(ErrorCodes.RecurringConflictReasonRoom, SchedulingAssert.ConflictReasons(ex));
        Assert.Equal(1, await w.CountAppointments());
    }

    #endregion

    #region Member selection change and removal

    [Fact]
    public async Task ChangeSelection_FutureOccurrence_RemovesUntouched_CancelsHistory_AddsNew_KeepsTheBooking_PastUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSelection_FutureOccurrence_RemovesUntouched_CancelsHistory_AddsNew_KeepsTheBooking_PastUnchanged));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        await Join(w, g.Group, ana, g.A, g.C);
        await Join(w, g.Group, marko, g.A);
        GenerateGroupAppointmentsResult past = await w.GenerateOccurrences(g.Group, SchedulingWorld.PastDay);
        Appointment pastOccurrence = await w.LoadAppointment(Assert.Single(past.Created).Id);
        Appointment future = await w.GenerateSingleOccurrence(g.Group);

        // History: Ana attended A in the past; Ana's FUTURE A was cancelled and confirmed again (touched, version 2).
        Guid pastA = BookingOf(pastOccurrence, ana).Participations.Single(p => p.AppointmentSegmentId == SegmentOf(pastOccurrence, g.A).Id).Id.Value;
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, pastA, new BookingSetStatusRequest { Status = BookingStatus.Completed });
        Guid futureAnaA = BookingOf(future, ana).Participations.Single(p => p.AppointmentSegmentId == SegmentOf(future, g.A).Id).Id.Value;
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, futureAnaA, new BookingSetStatusRequest { Status = BookingStatus.Cancelled });
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, futureAnaA, new BookingSetStatusRequest { Status = BookingStatus.Confirmed });
        Guid anaBooking = BookingOf(future, ana).Id.Value;
        Guid futureAnaC = BookingOf(future, ana).Participations.Single(p => p.AppointmentSegmentId == SegmentOf(future, g.C).Id).Id.Value;

        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, g.Group.Id);
        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, MemberId(detail, ana),
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.B, g.C } });
        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, MemberId(detail, marko),
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.B } });

        Appointment after = await w.LoadAppointment(future.Id.Value);
        Booking ana2 = BookingOf(after, ana);
        Assert.Equal(anaBooking, ana2.Id);
        Assert.Equal(ParticipationStatus.Cancelled, ana2.Participations.Single(p => p.Id == futureAnaA).Status); // history → cancelled
        Assert.Equal(ParticipationStatus.Confirmed, ana2.Participations.Single(p => p.Id == futureAnaC).Status); // C unchanged
        Assert.Equal(ParticipationStatus.Confirmed, ana2.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(after, g.B).Id).Status);
        // Marko's untouched A was removed; same Booking, now only B.
        Booking marko2 = BookingOf(after, marko);
        Assert.Equal(SegmentOf(after, g.B).Id, Assert.Single(marko2.Participations).AppointmentSegmentId);
        // The past occurrence is untouched.
        Appointment pastAfter = await w.LoadAppointment(pastOccurrence.Id.Value);
        Assert.Equal(ParticipationStatus.Completed, BookingOf(pastAfter, ana).Participations.Single(p => p.Id == pastA).Status);
        Assert.Equal(2, BookingOf(pastAfter, ana).Participations.Count);
    }

    [Fact]
    public async Task RemoveMember_CancelsEveryFutureParticipation_EachWithItsOwnSegmentLateness()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemoveMember_CancelsEveryFutureParticipation_EachWithItsOwnSegmentLateness));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A, g.C);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        // M1F.1: untouched participations are deleted on removal — give both business history first (cancelled and
        // confirmed again: StatusVersion 2), so the history-preserving cancellation path is exercised.
        foreach (BookingSegmentParticipation p in BookingOf(occurrence, ana).Participations)
        {
            await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, p.Id.Value, new BookingSetStatusRequest { Status = BookingStatus.Cancelled });
            await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, p.Id.Value, new BookingSetStatusRequest { Status = BookingStatus.Confirmed });
        }
        // Cutoff ends at 10:00: A (09:00) inside the late window, C (11:00) outside.
        await w.SetCancellationCutoffMinutes((int)(SchedulingWorld.Future(10) - DateTimeOffset.UtcNow).TotalMinutes);

        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, g.Group.Id);
        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, g.Group.Id, MemberId(detail, ana));

        Booking booking = BookingOf(await w.LoadAppointment(occurrence.Id.Value), ana);
        BookingSegmentParticipation a = booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(occurrence, g.A).Id);
        BookingSegmentParticipation c = booking.Participations.Single(p => p.AppointmentSegmentId == SegmentOf(occurrence, g.C).Id);
        Assert.Equal(ParticipationStatus.Cancelled, a.Status);
        Assert.True(a.IsLateCancellation);
        Assert.Equal(ParticipationStatus.Cancelled, c.Status);
        Assert.False(c.IsLateCancellation);
    }

    [Fact]
    public async Task LegacyMembershipWithoutSelection_OnAMultiTemplateGroup_IsRefused_NeverAllTemplates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LegacyMembershipWithoutSelection_OnAMultiTemplateGroup_IsRefused_NeverAllTemplates));
        Wellness g = await CreateWellness(w);

        await SchedulingAssert.BusinessRule(ErrorCodes.SegmentSelectionRequired, () => Join(w, g.Group, w.Client));
        await SchedulingAssert.BusinessRule(ErrorCodes.SegmentSelectionRequired, () => w.Groups.Update(w.OrganizationId, w.ActorUserId, g.Group.Id,
            new GroupUpdateRequest { Name = "x", CompanyId = w.Company.Id.Value, ServiceId = g.Yoga.Id.Value, Capacity = 3 }));
        Assert.Empty((await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members);
    }

    #endregion

    #region Waitlist, guest, close-out

    [Fact]
    public async Task Waitlist_IsPerSegment_PromotionFillsOnlyThatSegment_AndReusesTheBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Waitlist_IsPerSegment_PromotionFillsOnlyThatSegment_AndReusesTheBooking));
        Wellness g = await CreateWellness(w, capA: 1);
        Client holder = await w.AddClient("Holder");
        Client waiter = await w.AddClient("Waiter");
        await Join(w, g.Group, holder, g.A);
        await Join(w, g.Group, waiter, g.B);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        Guid segA = SegmentOf(occurrence, g.A).Id.Value;

        await SchedulingAssert.BusinessRule(ErrorCodes.SegmentSelectionRequired, () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true,
            occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value }));
        WaitlistEntryDto entry = await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new WaitlistJoinRequest { ClientId = waiter.Id.Value, SegmentId = segA });
        Assert.Equal(segA, entry.AppointmentSegmentId);
        // B has space — no waitlist there.
        await SchedulingAssert.BusinessRule(ErrorCodes.CapacityAvailable, () => w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true,
            occurrence.Id.Value, new WaitlistJoinRequest { ClientId = holder.Id.Value, SegmentId = SegmentOf(occurrence, g.B).Id }));

        Guid waiterBooking = BookingOf(occurrence, waiter).Id.Value;
        Guid holderA = Assert.Single(BookingOf(occurrence, holder).Participations).Id.Value;
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, holderA, new BookingSetStatusRequest { Status = BookingStatus.Cancelled });

        Appointment after = await w.LoadAppointment(occurrence.Id.Value);
        Booking promoted = BookingOf(after, waiter);
        Assert.Equal(waiterBooking, promoted.Id);
        Assert.Equal(2, promoted.Participations.Count);
        Assert.Equal(ParticipationStatus.Confirmed, promoted.Participations.Single(p => p.AppointmentSegmentId == segA).Status);
        Assert.Equal(WaitlistEntryStatus.Promoted, Assert.Single(await w.LoadWaitlist(occurrence.Id.Value)).Status);
    }

    [Fact]
    public async Task Guest_IsAddedToTheSelectedSegmentOnly_ReusesTheBooking_AndIsNotAMember()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Guest_IsAddedToTheSelectedSegmentOnly_ReusesTheBooking_AndIsNotAMember));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client guest = await w.AddClient("Guest");
        await Join(w, g.Group, ana, g.A);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        Guid segB = SegmentOf(occurrence, g.B).Id.Value;

        await SchedulingAssert.BusinessRule(ErrorCodes.SegmentSelectionRequired, () => w.Bookings.AddBooking(w.OrganizationId, w.ActorUserId, true,
            occurrence.Id.Value, new BookingCreateRequest { ClientId = guest.Id.Value }));

        BookingDto guestBooking = await w.Bookings.AddBooking(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new BookingCreateRequest { ClientId = guest.Id.Value, SegmentId = segB });
        Assert.Equal(segB, Assert.Single(guestBooking.Participations).AppointmentSegmentId);

        // A member's existing Booking is reused for a one-off extra segment (no membership change).
        Guid anaBooking = BookingOf(occurrence, ana).Id.Value;
        BookingDto anaDto = await w.Bookings.SetStatus(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, ana.Id.Value,
            new BookingSetStatusRequest { Status = BookingStatus.Confirmed, SegmentId = segB });
        Assert.Equal(anaBooking, anaDto.Id);
        Assert.Equal(2, anaDto.Participations.Count);

        GroupDetailDto detail = await w.Groups.GetById(w.OrganizationId, g.Group.Id);
        Assert.DoesNotContain(detail.Members, m => m.ClientId == guest.Id);
        Assert.Equal(new[] { g.A }, detail.Members.Single(m => m.ClientId == ana.Id).SegmentTemplateIds);
    }

    [Fact]
    public async Task CloseOut_CoversEverySegment_WarnsOnUnresolved_ExpiresEveryWaitlist_AndIsIdempotent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CloseOut_CoversEverySegment_WarnsOnUnresolved_ExpiresEveryWaitlist_AndIsIdempotent));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        Client waiter = await w.AddClient("Waiter");
        await Join(w, g.Group, ana, g.A);
        await Join(w, g.Group, marko, g.C);
        GenerateGroupAppointmentsResult past = await w.GenerateOccurrences(g.Group, SchedulingWorld.PastDay);
        Appointment occurrence = await w.LoadAppointment(Assert.Single(past.Created).Id);
        await using (DatabaseContext db = w.NewDb())
        {
            foreach (AppointmentSegment segment in occurrence.Segments)
                db.WaitlistEntries.Add(new WaitlistEntry
                {
                    Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, AppointmentId = occurrence.Id.Value, AppointmentSegmentId = segment.Id.Value,
                    ClientId = waiter.Id.Value, Status = WaitlistEntryStatus.Waiting, JoinedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
        }

        Guid anaA = Assert.Single(BookingOf(occurrence, ana).Participations).Id.Value;
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, anaA, new BookingSetStatusRequest { Status = BookingStatus.Completed });

        AppointmentDto closed = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);
        Assert.Contains(closed.Warnings, x => x.Code == WarningCodes.GroupAppointmentUnresolvedBookings); // only C (Marko) is unresolved
        // CHANGED in M1G: multi-template close-out computes session commission per segment — no blanket warning.
        Assert.DoesNotContain(closed.Warnings, x => x.Code == WarningCodes.GroupCommissionNotSupportedForMultiSegment);
        Appointment a1 = await w.LoadAppointment(occurrence.Id.Value);
        Assert.NotNull(a1.ClosedOutAt);
        Assert.Equal(AppointmentStatus.Scheduled, a1.Status); // derived: Marko is still Confirmed
        Assert.All(await w.LoadWaitlist(occurrence.Id.Value), e => Assert.Equal(WaitlistEntryStatus.Expired, e.Status));
        Assert.Equal(3, (await w.LoadWaitlist(occurrence.Id.Value)).Count);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);
        Assert.Equal(a1.ClosedOutAt, (await w.LoadAppointment(occurrence.Id.Value)).ClosedOutAt);
        Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "GroupClosedOut");
        Assert.Empty(await w.LoadCommissionEntries());

        Guid markoC = Assert.Single(BookingOf(occurrence, marko).Participations).Id.Value;
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, markoC, new BookingSetStatusRequest { Status = BookingStatus.NoShow });
        Assert.Equal(AppointmentStatus.Closed, (await w.LoadAppointment(occurrence.Id.Value)).Status);
    }

    #endregion

    #region Templates and read model

    [Fact]
    public async Task Templates_AnchorRule_LastTemplate_AndSelectedTemplates_AreProtected_ReadModelExposesThem()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Templates_AnchorRule_LastTemplate_AndSelectedTemplates_AreProtected_ReadModelExposesThem));
        ServiceEntity yoga = await w.AddGroupService(60);
        await SchedulingAssert.Validation(() => w.Groups.Create(w.OrganizationId, w.ActorUserId, new GroupCreateRequest
        {
            Name = "No anchor", CompanyId = w.Company.Id.Value,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(9) } },
            SegmentTemplates = new List<GroupSegmentTemplateRequest> { new() { ServiceId = yoga.Id.Value, StartOffsetMinutes = 15, Capacity = 3 } }
        }));

        Wellness g = await CreateWellness(w);
        Assert.Null(g.Group.ServiceId); // compatibility projection only for one template
        Assert.Null(g.Group.Capacity);
        Assert.Equal(new[] { 0, 60, 120 }, g.Group.SegmentTemplates.Select(t => t.StartOffsetMinutes));
        Assert.Equal(new[] { 15, 4, 8 }, g.Group.SegmentTemplates.Select(t => t.Capacity));

        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.C);
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Groups.RemoveSegmentTemplate(w.OrganizationId, w.ActorUserId, g.Group.Id, g.C));
        await SchedulingAssert.Validation(() => w.Groups.RemoveSegmentTemplate(w.OrganizationId, w.ActorUserId, g.Group.Id, g.A)); // anchor
        GroupDto afterRemoval = await w.Groups.RemoveSegmentTemplate(w.OrganizationId, w.ActorUserId, g.Group.Id, g.B);
        Assert.Equal(2, afterRemoval.SegmentTemplates.Count);

        GroupDto one = await w.CreateGroup(yoga, capacity: 4);
        await SchedulingAssert.BusinessRule(ErrorCodes.LastGroupSegmentTemplate,
            () => w.Groups.RemoveSegmentTemplate(w.OrganizationId, w.ActorUserId, one.Id, Assert.Single(one.SegmentTemplates).Id));
        Assert.Equal(yoga.Id, one.ServiceId);
        Assert.Equal(4, one.Capacity);
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task Race_TwoMembersForTheLastSeatOfATemplate_ExactlyOneJoins()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_TwoMembersForTheLastSeatOfATemplate_ExactlyOneJoins));
        Wellness g = await CreateWellness(w, capB: 1);
        Client first = await w.AddClient("First");
        Client second = await w.AddClient("Second");

        Exception[] outcomes = await Task.WhenAll(new[] { first, second }.Select(c => Task.Run(async () =>
        {
            using IServiceScope scope = SchedulingTestHost.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IGroupService>().AddMember(w.OrganizationId, w.ActorUserId, g.Group.Id,
                    new GroupMemberAddRequest { ClientId = c.Id.Value, SegmentTemplateIds = new List<Guid> { g.B } });
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })));

        Assert.Single(outcomes, o => o == null);
        Assert.Equal(ErrorCodes.GroupCapacityReached, Assert.IsType<BusinessRuleException>(Assert.Single(outcomes, o => o != null)).Code);
        Assert.Single((await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members);
    }

    #endregion
}
