using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1G — Group staffing per GroupSegmentTemplate (Employees[] + pricing source, copied into each generated segment),
/// hard rules per employee during generation, fixed group-session commission per (segment, employee) at close-out, the
/// explicit unsupported-percentage case, and the non-authoritative legacy DefaultTrainerId.
/// </summary>
public class MultiEmployeeGroupTests
{
    private sealed record Wellness(GroupDto Group, ServiceEntity Yoga, ServiceEntity Massage, ServiceEntity Recovery,
        Employee Ana, Employee Marko, Employee Ivana, Guid YogaT, Guid MassageT, Guid RecoveryT);

    private static GroupSegmentTemplateRequest Template(ServiceEntity service, int offset, int duration, Employee[] employees,
        SegmentPricingMode? mode = null, Employee pricing = null, Guid? roomId = null) => new()
    {
        ServiceId = service.Id.Value, StartOffsetMinutes = offset, DurationMinutes = duration, Capacity = 10, RoomId = roomId,
        EmployeeIds = employees.Select(e => e.Id.Value).ToList(), PricingMode = mode, PricingEmployeeId = pricing?.Id
    };

    private static Task<GroupDto> CreateGroup(SchedulingWorld w, params GroupSegmentTemplateRequest[] templates) =>
        w.Groups.Create(w.OrganizationId, w.ActorUserId, new GroupCreateRequest
        {
            Name = $"Wellness-{Guid.NewGuid():N}",
            CompanyId = w.Company.Id.Value,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = SchedulingWorld.FutureDay.DayOfWeek, StartTime = TimeSpan.FromHours(9) } },
            SegmentTemplates = templates.ToList()
        });

    /// <summary>Yoga 09:00-10:00 [Ana], Massage 10:00-10:30 [Marko], Recovery 10:30-11:15 [Ana, Ivana] (Standard pricing).</summary>
    private static async Task<Wellness> SetUp(SchedulingWorld w)
    {
        ServiceEntity yoga = await w.AddGroupService(60, 15m);
        ServiceEntity massage = await w.AddGroupService(30, 40m);
        ServiceEntity recovery = await w.AddGroupService(45, 20m);
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        GroupDto group = await CreateGroup(w,
            Template(yoga, 0, 60, new[] { ana }),
            Template(massage, 60, 30, new[] { marko }),
            Template(recovery, 90, 45, new[] { ana, ivana }, SegmentPricingMode.Standard));
        Guid Of(ServiceEntity s) => group.SegmentTemplates.Single(t => t.ServiceId == s.Id).Id;
        return new Wellness(group, yoga, massage, recovery, ana, marko, ivana, Of(yoga), Of(massage), Of(recovery));
    }

    private static Task<GroupDto> Join(SchedulingWorld w, GroupDto group, Client client, params Guid[] templates) =>
        w.Groups.AddMember(w.OrganizationId, w.ActorUserId, group.Id, new GroupMemberAddRequest
        {
            ClientId = client.Id.Value, SegmentTemplateIds = templates.ToList()
        });

    private static AppointmentSegment SegmentOf(Appointment occurrence, Guid templateId) =>
        occurrence.Segments.Single(s => s.GroupSegmentTemplateId == templateId);

    private static List<Guid> EmployeesOf(AppointmentSegment segment) => segment.Employees.Select(e => e.EmployeeId).OrderBy(x => x).ToList();

    private static List<Guid> Ids(params Employee[] employees) => employees.Select(e => e.Id.Value).OrderBy(x => x).ToList();

    [Fact]
    public async Task Generation_CopiesEachTemplatesStaffingAndPricingSource_AndSelectiveParticipationIsUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Generation_CopiesEachTemplatesStaffingAndPricingSource_AndSelectiveParticipationIsUnchanged));
        Wellness g = await SetUp(w);
        Client lea = await w.AddClient("Lea");
        await Join(w, g.Group, lea, g.YogaT, g.RecoveryT);

        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);

        Assert.Equal(Ids(g.Ana), EmployeesOf(SegmentOf(occurrence, g.YogaT)));
        Assert.Equal(Ids(g.Marko), EmployeesOf(SegmentOf(occurrence, g.MassageT)));
        Assert.Equal(Ids(g.Ana, g.Ivana), EmployeesOf(SegmentOf(occurrence, g.RecoveryT)));
        Assert.Equal((SegmentPricingMode.Employee, g.Ana.Id), (SegmentOf(occurrence, g.YogaT).PricingMode, SegmentOf(occurrence, g.YogaT).PricingEmployeeId));
        Assert.Equal((SegmentPricingMode.Standard, (Guid?)null), (SegmentOf(occurrence, g.RecoveryT).PricingMode, SegmentOf(occurrence, g.RecoveryT).PricingEmployeeId));

        List<BookingSegmentParticipation> participations = (await w.LoadParticipations(occurrence.Id.Value, lea));
        Assert.Equal(new[] { SegmentOf(occurrence, g.YogaT).Id, SegmentOf(occurrence, g.RecoveryT).Id }.OrderBy(x => x),
            participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));

        GroupSegmentTemplateDto recovery = g.Group.SegmentTemplates.Single(t => t.Id == g.RecoveryT);
        Assert.Equal(Ids(g.Ana, g.Ivana), recovery.Employees.Select(e => e.EmployeeId).OrderBy(x => x));
        Assert.Equal(SegmentPricingMode.Standard, recovery.PricingMode);
    }

    [Fact]
    public async Task Template_WithTwoEmployees_RequiresAnExplicitPricingSource_AndEveryEmployeeMustBeEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Template_WithTwoEmployees_RequiresAnExplicitPricingSource_AndEveryEmployeeMustBeEligible));
        ServiceEntity yoga = await w.AddGroupService();
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee restricted = await w.AddEmployeeRestrictedToAnotherService();

        ValidationAppException ex = await SchedulingAssert.Validation(() => CreateGroup(w, Template(yoga, 0, 60, new[] { ana, marko })));
        Assert.Equal(ErrorCodes.PricingSourceRequired, ex.Code);
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService,
            () => CreateGroup(w, Template(yoga, 0, 60, new[] { ana, restricted }, SegmentPricingMode.Employee, ana)));
        await SchedulingAssert.Validation(() => CreateGroup(w, Template(yoga, 0, 60, new[] { ana, ana }, SegmentPricingMode.Standard)));

        GroupDto trainerless = await CreateGroup(w, Template(yoga, 0, 60, Array.Empty<Employee>()));
        Assert.Equal(SegmentPricingMode.Standard, trainerless.SegmentTemplates.Single().PricingMode);
    }

    [Fact]
    public async Task Generation_RejectsAnEmployeeOverlappingAcrossSiblingTemplates_DifferentEmployeesMayOverlap()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Generation_RejectsAnEmployeeOverlappingAcrossSiblingTemplates_DifferentEmployeesMayOverlap));
        ServiceEntity yoga = await w.AddGroupService(60);
        ServiceEntity massage = await w.AddGroupService(60);
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);

        GroupDto sameAna = await CreateGroup(w, Template(yoga, 0, 60, new[] { ana }), Template(massage, 30, 60, new[] { ana }));
        await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict, () => w.GenerateOccurrences(sameAna, SchedulingWorld.FutureDay));

        GroupDto split = await CreateGroup(w, Template(yoga, 0, 60, new[] { ana }), Template(massage, 30, 60, new[] { marko }));
        Assert.Single((await w.GenerateOccurrences(split, SchedulingWorld.FutureDay.AddDays(7))).Created);
    }

    [Fact]
    public async Task Generation_ChecksEveryEmployeeOfATemplate_AndRoomPeopleCountBothEmployees()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Generation_ChecksEveryEmployeeOfATemplate_AndRoomPeopleCountBothEmployees));
        ServiceEntity yoga = await w.AddGroupService(60);
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Room room = await w.AddRoom(capacity: 3);
        Client c1 = await w.AddClient("C1");
        Client c2 = await w.AddClient("C2");

        // Marko is busy at 09:00 with an individual appointment → generation of the [Ana, Marko] template is rejected.
        await w.Appointments.Create(w.OrganizationId, w.ActorUserId, true, new AppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            Segments = new List<AppointmentSegmentCreateRequest>
            {
                new() { ServiceId = w.Service.Id.Value, PlannedStart = SchedulingWorld.Future(9), EmployeeIds = new List<Guid> { marko.Id.Value },
                    Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = w.Client.Id.Value } } }
            }
        });
        GroupDto busy = await CreateGroup(w, Template(yoga, 0, 60, new[] { ana, marko }, SegmentPricingMode.Standard));
        await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict, () => w.GenerateOccurrences(busy, SchedulingWorld.FutureDay));

        // Room capacity 3: 2 employees + 2 members = 4 → rejected; with one member (3 people) it fits.
        DateTimeOffset nextWeek = SchedulingWorld.FutureDay.AddDays(7);
        GroupDto roomed = await CreateGroup(w, Template(yoga, 0, 60, new[] { ana, marko }, SegmentPricingMode.Standard, roomId: room.Id));
        await Join(w, roomed, c1, roomed.SegmentTemplates.Single().Id);
        GroupDto full = await Join(w, roomed, c2, roomed.SegmentTemplates.Single().Id);
        await SchedulingAssert.BusinessRule(ErrorCodes.RecurringConflict, () => w.GenerateOccurrences(roomed, nextWeek));
        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, roomed.Id,
            (await w.Groups.GetById(w.OrganizationId, roomed.Id)).Members.Single(m => m.ClientId == c2.Id).Id);
        Assert.Single((await w.GenerateOccurrences(roomed, nextWeek)).Created);
        Assert.Equal(2, full.ActiveMemberCount);
    }

    [Fact]
    public async Task CloseOut_PaysFixedSessionCommission_OncePerSegmentAndEmployee_NotPerClient_AndIsIdempotent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CloseOut_PaysFixedSessionCommission_OncePerSegmentAndEmployee_NotPerClient_AndIsIdempotent));
        ServiceEntity yoga = await w.AddGroupService(60);
        ServiceEntity massage = await w.AddGroupService(30);
        ServiceEntity recovery = await w.AddGroupService(45);
        ServiceEntity stretch = await w.AddGroupService(15);
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        await w.AddCommissionRule(ana, yoga, CommissionCalculationType.Fixed, 10m);
        await w.AddCommissionRule(marko, massage, CommissionCalculationType.Fixed, 15m);
        await w.AddCommissionRule(ana, recovery, CommissionCalculationType.Fixed, 8m);
        await w.AddCommissionRule(ivana, recovery, CommissionCalculationType.Fixed, 12m);
        GroupDto group = await CreateGroup(w,
            Template(yoga, 0, 60, new[] { ana }),
            Template(massage, 60, 30, new[] { marko }),
            Template(recovery, 90, 45, new[] { ana, ivana }, SegmentPricingMode.Standard),
            Template(stretch, 135, 15, Array.Empty<Employee>())); // trainerless
        Guid[] all = group.SegmentTemplates.Select(t => t.Id).ToArray();
        await Join(w, group, w.Client, all);
        await Join(w, group, await w.AddClient("Second"), all);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        AppointmentDto closed = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value); // repeat

        Assert.DoesNotContain(closed.Warnings, x => x.Code == WarningCodes.GroupCommissionRuleNotSupported);
        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).Where(e => e.AppointmentId == occurrence.Id).ToList();
        Guid SegmentFor(ServiceEntity s) => occurrence.Segments.Single(x => x.ServiceId == s.Id).Id.Value;
        Assert.Equal(new[]
            {
                (SegmentFor(yoga), ana.Id.Value, 10m), (SegmentFor(massage), marko.Id.Value, 15m),
                (SegmentFor(recovery), ana.Id.Value, 8m), (SegmentFor(recovery), ivana.Id.Value, 12m)
            }.OrderBy(x => x.Item1).ThenBy(x => x.Item2),
            entries.Select(e => (e.AppointmentSegmentId.Value, e.EmployeeId, e.CommissionAmount)).OrderBy(x => x.Item1).ThenBy(x => x.Item2));
        Assert.All(entries, e => Assert.Equal(CommissionSourceType.GroupService, e.SourceType));
        Assert.DoesNotContain(entries, e => e.AppointmentSegmentId == SegmentFor(stretch));
    }

    [Fact]
    public async Task CloseOut_WithAPercentageGroupRule_DoesNotInventABase_AndWarnsExplicitly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CloseOut_WithAPercentageGroupRule_DoesNotInventABase_AndWarnsExplicitly));
        Wellness g = await SetUp(w);
        await w.AddCommissionRule(g.Ana, g.Recovery, CommissionCalculationType.Fixed, 8m);
        // Seeded directly: the rule service rejects Percentage for Group-mode services (COMMISSION_GROUP_PERCENTAGE_NOT_SUPPORTED);
        // this models a rule left behind by a later change of the service's execution mode.
        await w.AddCommissionRule(g.Ivana, g.Recovery, CommissionCalculationType.Percentage, 10m);
        await Join(w, g.Group, w.Client, g.YogaT, g.MassageT, g.RecoveryT);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);

        AppointmentDto closed = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        WarningDto warning = Assert.Single(closed.Warnings, x => x.Code == WarningCodes.GroupCommissionRuleNotSupported);
        WarningGroupCommissionRuleDetails details = Assert.IsType<WarningGroupCommissionRuleDetails>(warning.Details);
        Assert.Equal((SegmentOf(occurrence, g.RecoveryT).Id.Value, g.Ivana.Id.Value, "Percentage"), (details.SegmentId, details.EmployeeId, details.CalculationType));
        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).Where(e => e.AppointmentId == occurrence.Id).ToList();
        Assert.DoesNotContain(entries, e => e.EmployeeId == g.Ivana.Id); // not from a client, not from all clients, not as fixed
        Assert.Equal(8m, Assert.Single(entries).CommissionAmount);
    }

    [Fact]
    public async Task GroupUpdate_ChangesOnlyTheGroupsOwnFields_TemplateStaffIsChangedOnlyThroughTheTemplate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupUpdate_ChangesOnlyTheGroupsOwnFields_TemplateStaffIsChangedOnlyThroughTheTemplate));
        ServiceEntity yoga = await w.AddGroupService();

        GroupDto flat = await w.CreateGroup(yoga, 10);
        GroupSegmentTemplateDto only = flat.SegmentTemplates.Single();
        Assert.Equal(w.Employee.Id, Assert.Single(only.Employees).EmployeeId);
        Assert.Equal((SegmentPricingMode.Employee, w.Employee.Id), (only.PricingMode, only.PricingEmployeeId));

        // M1H: no flat trainer on the group — Update edits name/company/note only, the staff stays.
        GroupDto renamed = await w.Groups.Update(w.OrganizationId, w.ActorUserId, flat.Id,
            new GroupUpdateRequest { Name = "Flat", CompanyId = w.Company.Id.Value, Note = "note" });
        Assert.Equal(("Flat", "note"), (renamed.Name, renamed.Note));
        Assert.Equal(w.Employee.Id, Assert.Single(renamed.SegmentTemplates.Single().Employees).EmployeeId);

        // Clearing the staff is an explicit template edit.
        GroupDto cleared = await w.UpdateOnlyTemplate(renamed, r => r.EmployeeIds = new List<Guid>());
        Assert.Empty(cleared.SegmentTemplates.Single().Employees);
        Assert.Equal(SegmentPricingMode.Standard, cleared.SegmentTemplates.Single().PricingMode);

        Wellness g = await SetUp(w);
        GroupDto unchanged = await w.Groups.Update(w.OrganizationId, w.ActorUserId, g.Group.Id,
            new GroupUpdateRequest { Name = "Wellness 2", CompanyId = w.Company.Id.Value });
        Assert.Equal(2, unchanged.SegmentTemplates.Single(t => t.Id == g.RecoveryT).Employees.Count);
    }

    [Fact]
    public async Task TemplateStaffingChange_AffectsFutureGenerationOnly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TemplateStaffingChange_AffectsFutureGenerationOnly));
        Wellness g = await SetUp(w);
        Appointment first = await w.GenerateSingleOccurrence(g.Group);

        GroupSegmentTemplateDto yoga = g.Group.SegmentTemplates.Single(t => t.Id == g.YogaT);
        await w.Groups.UpdateSegmentTemplate(w.OrganizationId, w.ActorUserId, g.Group.Id, g.YogaT, new GroupSegmentTemplateRequest
        {
            ServiceId = yoga.ServiceId, StartOffsetMinutes = 0, DurationMinutes = 60, Capacity = 10,
            EmployeeIds = new List<Guid> { g.Ana.Id.Value, g.Marko.Id.Value }, PricingMode = SegmentPricingMode.Employee, PricingEmployeeId = g.Marko.Id
        });
        GenerateGroupAppointmentsResult next = await w.GenerateOccurrences(g.Group, SchedulingWorld.FutureDay.AddDays(7));

        Assert.Equal(Ids(g.Ana), EmployeesOf(SegmentOf(await w.LoadAppointment(first.Id.Value), g.YogaT)));
        AppointmentSegment generated = SegmentOf(await w.LoadAppointment(Assert.Single(next.Created).Id), g.YogaT);
        Assert.Equal(Ids(g.Ana, g.Marko), EmployeesOf(generated));
        Assert.Equal(g.Marko.Id, generated.PricingEmployeeId);
    }
}
