using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1F.1 — Group selective-participation consistency: occurrence generation vs membership/selection changes serialize
/// in one of the two legal orders (never a permanently stale occurrence); attendance "expected" is per segment and derived from
/// template selection (historical segments keep their participations as the truth; guests stay guests); removal uses the
/// centralized untouched/history rule so re-adding behaves consistently.
/// </summary>
public class MultiSegmentGroupConsistencyTests
{
    private sealed record Wellness(GroupDto Group, Guid A, Guid B, Guid C);

    private static async Task<Wellness> CreateWellness(SchedulingWorld w)
    {
        ServiceEntity yoga = await w.AddGroupService(60, 15m);
        ServiceEntity massage = await w.AddGroupService(30, 40m);
        ServiceEntity recovery = await w.AddGroupService(45, 20m);
        GroupDto group = await w.Groups.Create(w.OrganizationId, w.ActorUserId, new GroupCreateRequest
        {
            Name = $"Wellness-{Guid.NewGuid():N}",
            CompanyId = w.Company.Id.Value,
            Slots = new List<GroupSlotCreateRequest> { new() { DayOfWeek = SchedulingWorld.FutureDay.DayOfWeek, StartTime = TimeSpan.FromHours(9) } },
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new() { ServiceId = yoga.Id.Value, StartOffsetMinutes = 0, Capacity = 10, EmployeeIds = new List<Guid> { w.Employee.Id.Value } },
                new() { ServiceId = massage.Id.Value, StartOffsetMinutes = 60, DurationMinutes = 30, Capacity = 10, EmployeeIds = new List<Guid> { w.Employee.Id.Value } },
                new() { ServiceId = recovery.Id.Value, StartOffsetMinutes = 120, DurationMinutes = 45, Capacity = 10, EmployeeIds = new List<Guid> { w.Employee.Id.Value } }
            }
        });
        Guid Of(ServiceEntity s) => group.SegmentTemplates.Single(t => t.ServiceId == s.Id).Id;
        return new Wellness(group, Of(yoga), Of(massage), Of(recovery));
    }

    private static Task<GroupDto> Join(SchedulingWorld w, GroupDto group, Client client, params Guid[] templates) =>
        w.Groups.AddMember(w.OrganizationId, w.ActorUserId, group.Id, new GroupMemberAddRequest
        {
            ClientId = client.Id.Value, SegmentTemplateIds = templates.ToList()
        });

    private static async Task<Guid> MemberId(SchedulingWorld w, GroupDto group, Client client) =>
        (await w.Groups.GetById(w.OrganizationId, group.Id)).Members.Single(m => m.ClientId == client.Id).Id;

    private static AppointmentSegment SegmentOf(Appointment occurrence, Guid templateId) =>
        occurrence.Segments.Single(s => s.GroupSegmentTemplateId == templateId);

    /// <summary>Participations of the client on the (only) generated occurrence of the group on <paramref name="day"/>.</summary>
    private static async Task<(Appointment Occurrence, List<BookingSegmentParticipation> Participations, int Bookings)> ClientOn(
        SchedulingWorld w, GroupDto group, Client client, DateTimeOffset? day = null)
    {
        DateTimeOffset date = day ?? SchedulingWorld.FutureDay;
        await using DatabaseContext db = w.NewDb();
        Appointment occurrence = await db.Appointments.AsNoTracking()
            .Include(a => a.Segments).Include(a => a.Bookings)
            .SingleAsync(a => a.GroupId == group.Id && a.Segments.Any(s => s.PlannedStart >= date && s.PlannedStart < date.AddDays(1)));
        List<Booking> bookings = occurrence.Bookings.Where(b => b.ClientId == client.Id).ToList();
        return (occurrence, bookings.SelectMany(b => b.Participations).ToList(), bookings.Count);
    }

    #region Generation vs membership races

    private static async Task<Exception> InOwnScope(Func<IGroupService, Task> action)
    {
        using IServiceScope scope = SchedulingTestHost.CreateScope();
        try
        {
            await action(scope.ServiceProvider.GetRequiredService<IGroupService>());
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Gate: hold the group row FOR UPDATE. Generation (which has already read its membership snapshot) blocks at its FOR SHARE
    /// re-check and the membership change blocks at its version UPDATE — both past every pre-lock read. Released together, they
    /// serialize in whichever order the database picks; both legal orders must end in the same consistent result.
    /// </summary>
    private static async Task<Exception[]> RaceAtTheGroupRow(SchedulingWorld w, GroupDto group, Func<IGroupService, Task> membershipChange)
    {
        Task<Exception[]> race;
        await using (IUnitOfWork gate = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            int gatePid = await gate.Context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await gate.Context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM dunelight.groups WHERE id = {group.Id} FOR UPDATE");

            race = Task.WhenAll(
                Task.Run(() => InOwnScope(s => s.GenerateAppointments(w.OrganizationId, w.ActorUserId, new GenerateGroupAppointmentsRequest
                {
                    GroupId = group.Id, FromDate = SchedulingWorld.FutureDay, ToDate = SchedulingWorld.FutureDay
                }))),
                Task.Run(() => InOwnScope(membershipChange)));

            await WaitUntil(async () => await BlockedBehind(w, gatePid) >= 2);
            await gate.CommitAsync();
        }

        return await race;
    }

    [Fact]
    public async Task Race_AddMemberVsGeneration_TheOccurrenceContainsTheNewMemberExactlyOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_AddMemberVsGeneration_TheOccurrenceContainsTheNewMemberExactlyOnce));
        Wellness g = await CreateWellness(w);
        Client existing = await w.AddClient("Existing");
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, existing, g.A);

        Exception[] outcomes = await RaceAtTheGroupRow(w, g.Group, s => s.AddMember(w.OrganizationId, w.ActorUserId, g.Group.Id,
            new GroupMemberAddRequest { ClientId = ana.Id.Value, SegmentTemplateIds = new List<Guid> { g.A } }));

        Assert.All(outcomes, Assert.Null);
        (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(SegmentOf(occurrence, g.A).Id, Assert.Single(participations).AppointmentSegmentId);
        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task Race_SelectionExpansionVsGeneration_TheOccurrenceContainsTheNewTemplateExactlyOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_SelectionExpansionVsGeneration_TheOccurrenceContainsTheNewTemplateExactlyOnce));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A);
        Guid member = await MemberId(w, g.Group, ana);

        Exception[] outcomes = await RaceAtTheGroupRow(w, g.Group, s => s.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id,
            member, new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.A, g.B } }));

        Assert.All(outcomes, Assert.Null);
        (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(new[] { SegmentOf(occurrence, g.A).Id, SegmentOf(occurrence, g.B).Id }.OrderBy(x => x),
            participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));
    }

    [Fact]
    public async Task Race_RemoveMemberVsGeneration_TheOccurrenceAgreesWithTheFinalMembership()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_RemoveMemberVsGeneration_TheOccurrenceAgreesWithTheFinalMembership));
        Wellness g = await CreateWellness(w);
        Client stays = await w.AddClient("Stays");
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, stays, g.A);
        await Join(w, g.Group, ana, g.A, g.C);
        Guid member = await MemberId(w, g.Group, ana);

        Exception[] outcomes = await RaceAtTheGroupRow(w, g.Group, s => s.RemoveMember(w.OrganizationId, w.ActorUserId, g.Group.Id, member));

        Assert.All(outcomes, Assert.Null);
        // Either order: generated with Ana then her untouched participations removed, or generated without her.
        (_, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(0, bookings);
        Assert.Empty(participations);
        Assert.Single((await ClientOn(w, g.Group, stays)).Participations);
    }

    [Fact]
    public async Task Race_UngatedAddMemberVsGeneration_IsConsistentEveryRound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_UngatedAddMemberVsGeneration_IsConsistentEveryRound));
        const int rounds = 6;
        for (int round = 0; round < rounds; round++)
        {
            Wellness g = await CreateWellness(w);
            Client client = await w.AddClient($"R{round}");
            DateTimeOffset day = SchedulingWorld.FutureDay.AddDays(7 * round);

            Exception[] outcomes = await Task.WhenAll(
                Task.Run(() => InOwnScope(s => s.GenerateAppointments(w.OrganizationId, w.ActorUserId,
                    new GenerateGroupAppointmentsRequest { GroupId = g.Group.Id, FromDate = day, ToDate = day }))),
                Task.Run(() => InOwnScope(s => s.AddMember(w.OrganizationId, w.ActorUserId, g.Group.Id,
                    new GroupMemberAddRequest { ClientId = client.Id.Value, SegmentTemplateIds = new List<Guid> { g.A, g.C } }))));

            Assert.All(outcomes, Assert.Null);
            (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, client, day);
            Assert.Equal(1, bookings);
            Assert.Equal(new[] { SegmentOf(occurrence, g.A).Id, SegmentOf(occurrence, g.C).Id }.OrderBy(x => x),
                participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));
        }
    }

    /// <summary>
    /// Forced order "membership change first": generation has already read its snapshot (before its transaction) and is held
    /// at its first scheduling-subject lock (the trainer); the membership change, which never locks the trainer, commits in
    /// the meantime. On release generation must notice the stale snapshot and rebuild with the new member.
    /// </summary>
    [Fact]
    public async Task Race_ForcedOrder_AddMemberCommitsWhileGenerationHoldsAStaleSnapshot_GenerationIncludesTheMember()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_ForcedOrder_AddMemberCommitsWhileGenerationHoldsAStaleSnapshot_GenerationIncludesTheMember));
        Wellness g = await CreateWellness(w);
        Client existing = await w.AddClient("Existing");
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, existing, g.A);

        Task<Exception> generation;
        await using (IUnitOfWork gate = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            int gatePid = await gate.Context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await gate.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({SchedulingLockOrder.EmployeeKey(w.Employee.Id.Value)})");

            generation = Task.Run(() => InOwnScope(s => s.GenerateAppointments(w.OrganizationId, w.ActorUserId,
                new GenerateGroupAppointmentsRequest { GroupId = g.Group.Id, FromDate = SchedulingWorld.FutureDay, ToDate = SchedulingWorld.FutureDay })));
            await WaitUntil(async () => await BlockedBehind(w, gatePid) >= 1);

            Assert.Null(await InOwnScope(s => s.AddMember(w.OrganizationId, w.ActorUserId, g.Group.Id,
                new GroupMemberAddRequest { ClientId = ana.Id.Value, SegmentTemplateIds = new List<Guid> { g.A, g.C } })));
            Assert.Equal(0, await w.CountAppointments()); // nothing generated yet: AddMember had no occurrence to propagate into

            await gate.CommitAsync();
        }

        Assert.Null(await generation);
        (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(new[] { SegmentOf(occurrence, g.A).Id, SegmentOf(occurrence, g.C).Id }.OrderBy(x => x),
            participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));
    }

    /// <summary>Same forced order for a selection expansion (A → A+B) committing under generation's stale snapshot.</summary>
    [Fact]
    public async Task Race_ForcedOrder_SelectionExpansionCommitsWhileGenerationHoldsAStaleSnapshot_GenerationIncludesTheTemplate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_ForcedOrder_SelectionExpansionCommitsWhileGenerationHoldsAStaleSnapshot_GenerationIncludesTheTemplate));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A);
        Guid member = await MemberId(w, g.Group, ana);

        Task<Exception> generation;
        await using (IUnitOfWork gate = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            int gatePid = await gate.Context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await gate.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({SchedulingLockOrder.EmployeeKey(w.Employee.Id.Value)})");

            generation = Task.Run(() => InOwnScope(s => s.GenerateAppointments(w.OrganizationId, w.ActorUserId,
                new GenerateGroupAppointmentsRequest { GroupId = g.Group.Id, FromDate = SchedulingWorld.FutureDay, ToDate = SchedulingWorld.FutureDay })));
            await WaitUntil(async () => await BlockedBehind(w, gatePid) >= 1);

            Assert.Null(await InOwnScope(s => s.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, member,
                new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.A, g.B } })));

            await gate.CommitAsync();
        }

        Assert.Null(await generation);
        (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(new[] { SegmentOf(occurrence, g.A).Id, SegmentOf(occurrence, g.B).Id }.OrderBy(x => x),
            participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));
    }

    /// <summary>
    /// Forced order "generation first": AddMember has started but is held at its client lock; generation (which does not lock
    /// the not-yet-member) commits the occurrence without her. On release AddMember must see the committed occurrence and
    /// propagate into it.
    /// </summary>
    [Fact]
    public async Task Race_ForcedOrder_GenerationCommitsWhileAddMemberIsInFlight_AddMemberPropagatesIntoTheOccurrence()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Race_ForcedOrder_GenerationCommitsWhileAddMemberIsInFlight_AddMemberPropagatesIntoTheOccurrence));
        Wellness g = await CreateWellness(w);
        Client existing = await w.AddClient("Existing");
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, existing, g.A);

        Task<Exception> addMember;
        await using (IUnitOfWork gate = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            int gatePid = await gate.Context.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            await gate.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({SchedulingLockOrder.ClientKey(ana.Id.Value)})");

            addMember = Task.Run(() => InOwnScope(s => s.AddMember(w.OrganizationId, w.ActorUserId, g.Group.Id,
                new GroupMemberAddRequest { ClientId = ana.Id.Value, SegmentTemplateIds = new List<Guid> { g.A, g.C } })));
            await WaitUntil(async () => await BlockedBehind(w, gatePid) >= 1);

            Assert.Null(await InOwnScope(s => s.GenerateAppointments(w.OrganizationId, w.ActorUserId,
                new GenerateGroupAppointmentsRequest { GroupId = g.Group.Id, FromDate = SchedulingWorld.FutureDay, ToDate = SchedulingWorld.FutureDay })));
            Assert.Equal(0, (await ClientOn(w, g.Group, ana)).Bookings); // generated from the old membership

            await gate.CommitAsync();
        }

        Assert.Null(await addMember);
        (Appointment occurrence, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(new[] { SegmentOf(occurrence, g.A).Id, SegmentOf(occurrence, g.C).Id }.OrderBy(x => x),
            participations.Select(p => (Guid?)p.AppointmentSegmentId).OrderBy(x => x));
        Assert.All(participations, p => Assert.Equal(ParticipationStatus.Confirmed, p.Status));
    }

    /// <summary>Sessions waiting (directly or behind another waiter) on the gate session.</summary>
    private static async Task<int> BlockedBehind(SchedulingWorld w, int gatePid)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.Database.SqlQuery<int>($@"
            WITH RECURSIVE blocked(pid) AS (
                SELECT a.pid FROM pg_stat_activity a WHERE {gatePid} = ANY(pg_blocking_pids(a.pid))
                UNION
                SELECT a.pid FROM pg_stat_activity a JOIN blocked b ON b.pid = ANY(pg_blocking_pids(a.pid)))
            SELECT count(*)::int AS ""Value"" FROM blocked").SingleAsync();
    }

    private static async Task WaitUntil(Func<Task<bool>> condition)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!await condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    #endregion

    #region Selective attendance

    [Fact]
    public async Task Attendance_ExpectedAndRecorded_ArePerSegment_AndFollowTemplateSelection()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Attendance_ExpectedAndRecorded_ArePerSegment_AndFollowTemplateSelection));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        Client marko = await w.AddClient("Marko");
        Client ivana = await w.AddClient("Ivana");
        Client guest = await w.AddClient("Guest");
        await Join(w, g.Group, ana, g.A, g.C);
        await Join(w, g.Group, marko, g.A, g.B);
        await Join(w, g.Group, ivana, g.B);
        Appointment occurrence = await w.GenerateSingleOccurrence(g.Group);
        // A gap: Marko's A participation is missing (e.g. a legacy shape) → he is EXPECTED on A only.
        await using (DatabaseContext db = w.NewDb())
        {
            await db.BookingSegmentParticipations
                .Where(p => p.AppointmentSegmentId == SegmentOf(occurrence, g.A).Id && p.Booking.ClientId == marko.Id)
                .ExecuteDeleteAsync();
        }
        await w.Bookings.AddGroupGuest(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new BookingCreateRequest { ClientId = guest.Id.Value, SegmentId = SegmentOf(occurrence, g.B).Id });

        GroupAttendanceListDto list = await w.GroupAttendance.GetAttendance(w.OrganizationId, occurrence.Id.Value);

        GroupSegmentAttendanceDto a = list.Segments.Single(s => s.SegmentTemplateId == g.A);
        GroupSegmentAttendanceDto b = list.Segments.Single(s => s.SegmentTemplateId == g.B);
        GroupSegmentAttendanceDto c = list.Segments.Single(s => s.SegmentTemplateId == g.C);
        Assert.Equal(new[] { ana.Id }, a.Recorded.Select(e => (Guid?)e.ClientId));
        Assert.Equal(new[] { marko.Id }, a.Expected.Select(e => (Guid?)e.ClientId)); // selected A, no participation yet
        Assert.Equal(new[] { guest.Id, ivana.Id, marko.Id }.OrderBy(x => x), b.Recorded.Select(e => (Guid?)e.ClientId).OrderBy(x => x));
        Assert.Empty(b.Expected); // Ana selected only A + C — never expected on B
        Assert.False(b.Recorded.Single(e => e.ClientId == guest.Id).IsMember); // guest stays a guest
        Assert.True(b.Recorded.Single(e => e.ClientId == ivana.Id).IsMember);
        Assert.All(b.Recorded, e => Assert.NotNull(e.ParticipationId));
        Assert.Equal(new[] { ana.Id }, c.Recorded.Select(e => (Guid?)e.ClientId));
        Assert.Empty(c.Expected);
        // M1H: there is no occurrence-level summary any more — attendance is per segment only.
        Assert.Equal(3, list.Segments.Count);
    }

    [Fact]
    public async Task Attendance_OfAHistoricalOccurrence_IsNotRewrittenByLaterSelectionEdits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Attendance_OfAHistoricalOccurrence_IsNotRewrittenByLaterSelectionEdits));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A, g.C);
        GenerateGroupAppointmentsResult past = await w.GenerateOccurrences(g.Group, SchedulingWorld.PastDay);
        Guid pastId = Assert.Single(past.Created).Id;
        GroupAttendanceListDto before = await w.GroupAttendance.GetAttendance(w.OrganizationId, pastId);

        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, await MemberId(w, g.Group, ana),
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.B } });
        GroupAttendanceListDto after = await w.GroupAttendance.GetAttendance(w.OrganizationId, pastId);

        foreach (GroupSegmentAttendanceDto segment in after.Segments)
        {
            GroupSegmentAttendanceDto then = before.Segments.Single(s => s.SegmentId == segment.SegmentId);
            Assert.Equal(then.Recorded.Select(e => e.ParticipationId), segment.Recorded.Select(e => e.ParticipationId));
            Assert.Empty(segment.Expected); // past segments: participations are the only truth
        }
        Assert.Equal(new[] { ana.Id }, after.Segments.Single(s => s.SegmentTemplateId == g.A).Recorded.Select(e => (Guid?)e.ClientId));
        Assert.Empty(after.Segments.Single(s => s.SegmentTemplateId == g.B).Recorded);
    }

    #endregion

    #region Remove / re-add

    [Fact]
    public async Task ReAdd_AfterAnUntouchedRemoval_CreatesExactlyOneConfirmedParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReAdd_AfterAnUntouchedRemoval_CreatesExactlyOneConfirmedParticipation));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A, g.C);
        await w.GenerateSingleOccurrence(g.Group);

        // Selection removal of an untouched participation deletes it; re-selecting recreates it on the SAME Booking.
        Guid member = await MemberId(w, g.Group, ana);
        Guid bookingBefore = (await w.LoadAppointment((await ClientOn(w, g.Group, ana)).Occurrence.Id.Value)).Bookings.Single(b => b.ClientId == ana.Id).Id.Value;
        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, member,
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.C } });
        Assert.Single((await ClientOn(w, g.Group, ana)).Participations);
        await w.Groups.ChangeMemberSegmentTemplates(w.OrganizationId, w.ActorUserId, g.Group.Id, member,
            new GroupMemberSegmentTemplatesRequest { SegmentTemplateIds = new List<Guid> { g.A, g.C } });
        (Appointment occurrence, List<BookingSegmentParticipation> afterReselect, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(bookingBefore, (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(b => b.ClientId == ana.Id).Id);
        Assert.Equal(2, afterReselect.Count);
        Assert.All(afterReselect, p => Assert.Equal(ParticipationStatus.Confirmed, p.Status));

        // Member removal deletes untouched participations (and the empty Booking); re-adding recreates them.
        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, g.Group.Id, member);
        Assert.Equal(0, (await ClientOn(w, g.Group, ana)).Bookings);
        await Join(w, g.Group, ana, g.A);
        (Appointment again, List<BookingSegmentParticipation> readded, int bookingsAgain) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookingsAgain);
        BookingSegmentParticipation p = Assert.Single(readded);
        Assert.Equal(SegmentOf(again, g.A).Id, p.AppointmentSegmentId);
        Assert.Equal(ParticipationStatus.Confirmed, p.Status);
    }

    [Fact]
    public async Task ReAdd_AfterAHistoryPreservingCancellation_DoesNotReactivate_AndNeverDuplicates()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ReAdd_AfterAHistoryPreservingCancellation_DoesNotReactivate_AndNeverDuplicates));
        Wellness g = await CreateWellness(w);
        Client ana = await w.AddClient("Ana");
        await Join(w, g.Group, ana, g.A);
        await w.GenerateSingleOccurrence(g.Group);
        (Appointment occurrence, _, _) = await ClientOn(w, g.Group, ana);
        Booking booking = (await w.LoadAppointment(occurrence.Id.Value)).Bookings.Single(b => b.ClientId == ana.Id);
        await w.PayBookingViaCheckout(booking.Id.Value, ana, 5m); // business history

        await w.Groups.RemoveMember(w.OrganizationId, w.ActorUserId, g.Group.Id, await MemberId(w, g.Group, ana));
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single((await ClientOn(w, g.Group, ana)).Participations).Status);

        await Join(w, g.Group, ana, g.A);

        // PINNED (M1F.1): there is no reliable provenance that this cancellation came from the membership removal (a future
        // participation may also be cancelled as a one-off occurrence decision), so re-adding does NOT reactivate it — the
        // history stays, nothing is duplicated; reactivation is an explicit participation transition.
        (_, List<BookingSegmentParticipation> participations, int bookings) = await ClientOn(w, g.Group, ana);
        Assert.Equal(1, bookings);
        Assert.Equal(ParticipationStatus.Cancelled, Assert.Single(participations).Status);
        Assert.Contains((await w.Groups.GetById(w.OrganizationId, g.Group.Id)).Members, m => m.ClientId == ana.Id);
    }

    #endregion
}
