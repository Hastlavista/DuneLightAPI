#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.K2;

/// <summary>
/// K2 (ADR-0032) — granularne ovlasti: zatvoren termin (ručno ili automatski na kraju poslovnog dana) i korekcije po izvornom
/// statusu, ponovno otvaranje, otpis naknade vs. jedinice, rad izvan radnog vremena kao zaseban grant, roster u prošlosti.
/// </summary>
public class K2PermissionsTests
{
    private static readonly DateOnly LongValid = new(2035, 1, 1);

    /// <summary>403 čija poruka navodi grant koji nedostaje (pregled K2: svaki novi grant ima barem jedan takav test).</summary>
    private static async Task Refused(string grant, Func<Task> action)
    {
        ForbiddenAppException ex = await Assert.ThrowsAsync<ForbiddenAppException>(action);
        Assert.Contains(grant, ex.Message);
    }

    #region AppointmentClosure (čista pravila)

    private static Appointment InMemory(DateTimeOffset start, DateTimeOffset end, DateTimeOffset createdAt) => new()
    {
        CreatedAt = createdAt,
        Segments = new List<AppointmentSegment> { new() { PlannedStart = start, PlannedEnd = end } }
    };

    [Fact]
    public void AutoClose_IsLocalMidnightAfterTheLatestOf_SegmentEnd_Creation_AndReopen()
    {
        OrganizationCalendar zagreb = OrganizationCalendar.For("Europe/Zagreb");
        DateTimeOffset end = new(2031, 3, 3, 21, 30, 0, TimeSpan.Zero); // 22:30 lokalno (CET)
        Appointment a = InMemory(end.AddHours(-1), end, createdAt: new DateTimeOffset(2031, 3, 1, 8, 0, 0, TimeSpan.Zero));

        // Ponoć 4.3. lokalno = 23:00 UTC 3.3.
        Assert.Equal(new DateTimeOffset(2031, 3, 3, 23, 0, 0, TimeSpan.Zero), AppointmentClosure.AutoClosesAt(a, zagreb));

        // Termin upisan unatrag ostaje otvoren do kraja dana upisa.
        a.CreatedAt = new DateTimeOffset(2031, 3, 10, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2031, 3, 10, 23, 0, 0, TimeSpan.Zero), AppointmentClosure.AutoClosesAt(a, zagreb));

        // Ponovno otvaranje pomiče automatsko zatvaranje na kraj tog dana; ručno zatvaranje zatvara odmah.
        a.ReopenedAt = new DateTimeOffset(2031, 3, 12, 9, 0, 0, TimeSpan.Zero);
        Assert.False(AppointmentClosure.IsClosed(a, zagreb, new DateTimeOffset(2031, 3, 12, 22, 59, 0, TimeSpan.Zero)));
        Assert.True(AppointmentClosure.IsClosed(a, zagreb, new DateTimeOffset(2031, 3, 12, 23, 0, 0, TimeSpan.Zero)));
        a.ClosedAt = new DateTimeOffset(2031, 3, 12, 10, 0, 0, TimeSpan.Zero);
        Assert.True(AppointmentClosure.IsClosed(a, zagreb, new DateTimeOffset(2031, 3, 12, 10, 1, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Reopen_NeedsTheCorrectionGrantOfEveryTerminalStatusPresent_AndAReason()
    {
        GrantContext completedOnly = new(new HashSet<string> { Grants.AppointmentsCorrectionsCompleted });
        ParticipationStatus[] statuses = { ParticipationStatus.Completed, ParticipationStatus.NoShow, ParticipationStatus.Confirmed };

        ForbiddenAppException missing = Assert.Throws<ForbiddenAppException>(
            () => AppointmentClosure.EnsureReopenAllowed(completedOnly, statuses, "razlog"));
        Assert.Contains(Grants.AppointmentsCorrectionsNoShow, missing.Message);

        GrantContext both = new(new HashSet<string> { Grants.AppointmentsCorrectionsCompleted, Grants.AppointmentsCorrectionsNoShow });
        ValidationAppException noReason = Assert.Throws<ValidationAppException>(() => AppointmentClosure.EnsureReopenAllowed(both, statuses, " "));
        Assert.Equal(ErrorCodes.CorrectionReasonRequired, noReason.Code);
        AppointmentClosure.EnsureReopenAllowed(both, statuses, "razlog");

        // Bez terminalnih sudjelovanja dovoljan je bilo koji grant korekcije.
        AppointmentClosure.EnsureReopenAllowed(completedOnly, new[] { ParticipationStatus.Confirmed }, "razlog");
        Assert.Throws<ForbiddenAppException>(() => AppointmentClosure.EnsureReopenAllowed(
            new GrantContext(new HashSet<string>()), new[] { ParticipationStatus.Confirmed }, "razlog"));
    }

    #endregion

    #region Korekcije prije i nakon zatvaranja

    [Fact]
    public async Task BeforeTheAppointmentIsClosed_ChangingAStatus_IsMarking_WithoutAGrantOrAReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BeforeTheAppointmentIsClosed_ChangingAStatus_IsMarking_WithoutAGrantOrAReason));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10)); // upisan danas → otvoren do ponoći

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed);
        BookingDto back = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, back.Status);
        Assert.False((await w.Appointments.GetById(w.OrganizationId, created.Id)).IsClosed);
        Assert.DoesNotContain(await w.LoadAuditLog(created.Id), a => a.ChangeType == "StatusCorrectedAfterClose");
    }

    [Fact]
    public async Task ClosedAppointment_CorrectionNeedsTheGrantOfTheOriginalStatus_AndAReason_AuditedAsACorrection_CommissionReversalLinked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClosedAppointment_CorrectionNeedsTheGrantOfTheOriginalStatus_AndAReason_AuditedAsACorrection_CommissionReversalLinked));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));

        AppointmentDto closed = await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, created.Id);
        Assert.True(closed.IsClosed);
        Assert.Equal(w.ActorUserId, closed.ClosedBy);
        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "AppointmentClosed");

        // Grant za drugi izvorni status ne pomaže.
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsCorrectionsNoShow);
        await Refused(Grants.AppointmentsCorrectionsCompleted, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "kriva osoba"));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsCorrectionsCompleted);
        ValidationAppException noReason = await SchedulingAssert.Validation(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed));
        Assert.Equal(ErrorCodes.CorrectionReasonRequired, noReason.Code);
        Assert.Equal(BookingStatus.Completed, (await w.LoadBooking(created.Id, w.Client)).Status);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "kriva osoba");

        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "StatusCorrectedAfterClose"
            && a.OldValue == "Completed" && a.NewValue == "Confirmed|kriva osoba" && a.ChangedBy == w.ActorUserId);
        CommissionEntry commission = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(CommissionEntryStatus.Reversed, commission.Status);
        Assert.Equal("Korekcija statusa Completed -> Confirmed (StatusVersion 2): kriva osoba", commission.ReversalReason);
    }

    [Fact]
    public async Task ClosedAppointment_AConfirmedParticipationIsStillMarked_WithoutAGrant_AuditedAsMarkedAfterClose()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClosedAppointment_AConfirmedParticipationIsStillMarked_WithoutAGrant_AuditedAsMarkedAfterClose));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, created.Id);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "MarkedAfterClose" && a.OldValue == "Confirmed" && a.NewValue == "NoShow");
        // Ispravak tog izostanka je sad korekcija zatvorenog termina.
        await Refused(Grants.AppointmentsCorrectionsNoShow, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, correctionReason: "ipak došao"));
    }

    [Fact]
    public async Task AutomaticClose_AtTheEndOfTheBusinessDay_LocksCorrections_WithoutAnyJob()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AutomaticClose_AtTheEndOfTheBusinessDay_LocksCorrections_WithoutAnyJob));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        await using (DatabaseContext db = w.NewDb()) // upis termina pomaknut u prošlost — setup, ne ponašanje
        {
            Appointment a = await db.Appointments.SingleAsync(x => x.Id == created.Id);
            a.CreatedAt = SchedulingWorld.PastDay;
            await db.SaveChangesAsync();
        }

        AppointmentDto read = await w.Appointments.GetById(w.OrganizationId, created.Id);
        Assert.True(read.IsClosed);
        Assert.Null(read.ClosedAt);
        Assert.Equal(SchedulingWorld.PastDay.AddDays(1), read.AutoClosesAt);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "x"));
    }

    [Fact]
    public async Task Reopen_NeedsAClosedAppointment_TheCorrectionGrant_AndAReason_ThenCorrectionIsFreeUntilClosedAgain()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reopen_NeedsAClosedAppointment_TheCorrectionGrant_AndAReason_ThenCorrectionIsFreeUntilClosedAgain));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotClosed, async () =>
        {
            await w.GrantUser(w.ActorUserId, Grants.AppointmentsCorrectionsCompleted);
            await w.Appointments.Reopen(w.OrganizationId, w.ActorUserId, created.Id, new AppointmentReopenRequest { Reason = "x" });
        });

        await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, created.Id);
        await SchedulingAssert.Validation(() => w.Appointments.Reopen(w.OrganizationId, w.ActorUserId, created.Id, new AppointmentReopenRequest()));
        Guid other = await w.AddMemberUser();
        await w.GrantUser(other, Grants.AppointmentsWriteAll, Grants.AppointmentsCorrectionsNoShow);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => w.Appointments.Reopen(w.OrganizationId, other, created.Id, new AppointmentReopenRequest { Reason = "x" }));

        AppointmentDto reopened = await w.Appointments.Reopen(w.OrganizationId, w.ActorUserId, created.Id, new AppointmentReopenRequest { Reason = "krivi klijent" });

        Assert.False(reopened.IsClosed);
        Assert.Null(reopened.ClosedAt);
        Assert.Equal(("krivi klijent", w.ActorUserId), (reopened.ReopenReason, reopened.ReopenedBy.Value));
        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "AppointmentReopened" && a.NewValue == "krivi klijent");
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // otvoren: bez razloga
        Assert.True((await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, created.Id)).IsClosed);
    }

    [Fact]
    public async Task GroupCloseOut_ClosesTheAppointment_AndAReopenedGroupClosedAgain_EarnsNoSecondCommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCloseOut_ClosesTheAppointment_AndAReopenedGroupClosedAgain_EarnsNoSecondCommission));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll, Grants.AppointmentsCorrectionsCompleted);
        var svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, 3);
        Client member = w.Client;
        await w.AddGroupMember(group, member);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed);

        AppointmentDto closed = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);
        Assert.True(closed.IsClosed);
        await w.Appointments.Reopen(w.OrganizationId, w.ActorUserId, occurrence.Id.Value, new AppointmentReopenRequest { Reason = "ispravak" });
        await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        Assert.Equal(CommissionEntryStatus.Earned, Assert.Single(await w.LoadCommissionEntries()).Status);
    }

    [Fact]
    public async Task Reopen_WithACompletedAndANoShowParticipation_NeedsBothCorrectionGrants()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reopen_WithACompletedAndANoShowParticipation_NeedsBothCorrectionGrants));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: second);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed);
        await w.SetBookingStatus(created.Id, second, BookingStatus.NoShow);
        await w.Appointments.Close(w.OrganizationId, w.ActorUserId, true, created.Id);

        Guid user = await w.AddMemberUser();
        await w.GrantUser(user, Grants.AppointmentsWriteAll, Grants.AppointmentsCorrectionsCompleted);
        await Refused(Grants.AppointmentsCorrectionsNoShow,
            () => w.Appointments.Reopen(w.OrganizationId, user, created.Id, new AppointmentReopenRequest { Reason = "ispravak" }));
        Assert.True((await w.Appointments.GetById(w.OrganizationId, created.Id)).IsClosed);

        await w.GrantUser(user, Grants.AppointmentsCorrectionsNoShow);
        AppointmentDto reopened = await w.Appointments.Reopen(w.OrganizationId, user, created.Id, new AppointmentReopenRequest { Reason = "ispravak" });
        Assert.False(reopened.IsClosed);
    }

    #endregion

    #region Otpis naknade vs. jedinice (12.2)

    [Fact]
    public async Task WaiverAtEventTime_OfAPackageUnit_NeedsUnitWaive_FeeWaiveIsNotEnough_AndTheWholeCommandIsRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaiverAtEventTime_OfAPackageUnit_NeedsUnitWaive_FeeWaiveIsNotEnough_AndTheWholeCommandIsRefused));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        ClientPackage counted = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyFeeWaive);

        ForbiddenAppException refused = await Assert.ThrowsAsync<ForbiddenAppException>(() => w.SetBookingStatus(
            created.Id, w.Client, BookingStatus.NoShow, waivePolicyConsequence: true, waiverReason: "prvi put"));
        Assert.Contains(Grants.AppointmentsPolicyUnitWaive, refused.Message);
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status); // ništa nije izvršeno
        Assert.Empty(await w.LoadPolicyConsequences(created.Id));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyUnitWaive);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, waivePolicyConsequence: true, waiverReason: "prvi put");

        Assert.Equal(PolicyConsequenceStatus.Waived, Assert.Single(await w.LoadPolicyConsequences(created.Id)).Status);
        Assert.Equal(5, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task WaiverAfterTheEvent_OfAFee_NeedsFeeWaive_UnitWaiveIsNotEnough()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaiverAfterTheEvent_OfAFee_NeedsFeeWaive_UnitWaiveIsNotEnough));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll, Grants.AppointmentsPolicyUnitWaive);
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);
        Guid participationId = created.Bookings.Single().Participations.Single().Id;

        await Refused(Grants.AppointmentsPolicyFeeWaive, () => w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, participationId,
            new PolicyConsequenceWaiveRequest { WaiverReason = "dobra volja" }));
        Assert.Equal(PolicyConsequenceStatus.Active, Assert.Single(await w.LoadPolicyConsequences(created.Id)).Status);

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyFeeWaive);
        await w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, participationId, new PolicyConsequenceWaiveRequest { WaiverReason = "dobra volja" });
        Assert.Equal(PolicyConsequenceStatus.Waived, Assert.Single(await w.LoadPolicyConsequences(created.Id)).Status);
    }

    #endregion

    #region Rad izvan radnog vremena (P-2) i "vrati termin"

    [Fact]
    public async Task Override_WithoutTheGrant_IsRefused_AndEveryRealOverrideIsAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Override_WithoutTheGrant_IsRefused_AndEveryRealOverrideIsAudited));
        Guid noGrant = await w.AddMemberUser();

        await Refused(Grants.AppointmentsAvailabilityOverride, () => w.Appointments.Create(w.OrganizationId, noGrant, true,
            w.CreateRequest(SchedulingWorld.Future(22), overrideAvailability: true).ToTarget()));

        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(22), overrideAvailability: true));
        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "AvailabilityOverride"
            && a.NewValue == WarningCodes.OutsideWorkingHours && a.ChangedBy == w.ActorUserId);

        // Override koji ništa nije zaobišao se ne bilježi.
        AppointmentDto inHours = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), overrideAvailability: true));
        Assert.DoesNotContain(await w.LoadAuditLog(inHours.Id), a => a.ChangeType == "AvailabilityOverride");
    }

    [Fact]
    public async Task GroupGeneration_OverrideWithoutTheGrant_IsRefused_GroupsManageIsNotEnough_AndAHolidayOverrideIsAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupGeneration_OverrideWithoutTheGrant_IsRefused_GroupsManageIsNotEnough_AndAHolidayOverrideIsAudited));
        var svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, 3);
        await w.AddCompanyHoliday(w.Company, SchedulingWorld.FutureDay);
        Guid manager = await w.AddMemberUser();
        await w.GrantUser(manager, Grants.GroupsManage);

        await Refused(Grants.AppointmentsAvailabilityOverride, () => w.Groups.GenerateAppointments(w.OrganizationId, manager,
            new Core.DTOs.Groups.GenerateGroupAppointmentsRequest
            {
                GroupId = group.Id, FromDate = SchedulingWorld.Day(SchedulingWorld.FutureDay), ToDate = SchedulingWorld.Day(SchedulingWorld.FutureDay), OverrideAvailability = true
            }));

        var result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay, overrideAvailability: true); // akter ima grant
        Guid occurrenceId = Assert.Single(result.Created).Id;
        Assert.Contains(await w.LoadAuditLog(occurrenceId), a => a.ChangeType == "AvailabilityOverride"
            && a.NewValue == WarningCodes.CompanyClosedHoliday && a.ChangedBy == w.ActorUserId);
    }

    [Fact]
    public async Task Restore_NeedsCorrectionsCancelled_AndChecksWorkingHoursLikeANewBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_NeedsCorrectionsCancelled_AndChecksWorkingHoursLikeANewBooking));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        Assert.False((await w.Appointments.GetById(w.OrganizationId, created.Id)).IsClosed); // i prije zatvaranja
        await Refused(Grants.AppointmentsCorrectionsCancelled, () => w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id, null));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsCorrectionsCancelled);
        await w.SetWorkingHours(w.Employee, TimeSpan.FromHours(12), TimeSpan.FromHours(18)); // termin u 10 je sad izvan radnog vremena
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id, null));

        AppointmentDto restored = await w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id,
            new AppointmentRestoreRequest { OverrideAvailability = true });
        Assert.Equal(AppointmentStatus.Scheduled, restored.Status);
        Assert.Contains(restored.Warnings, x => x.Code == WarningCodes.OutsideWorkingHours);
        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "AvailabilityOverride");
    }

    #endregion

    #region Roster u prošlosti (P-4)

    [Fact]
    public async Task RosterEntry_StartingBeforeToday_NeedsWritePast_TodayAndFutureDoNot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RosterEntry_StartingBeforeToday_NeedsWritePast_TodayAndFutureDoNot));
        RosterType absence = await w.AddRosterType("Bolovanje", isAbsence: true);
        IRosterEntryService roster = w.Resolve<IRosterEntryService>();
        DateOnly today = SchedulingWorld.Day(TestClock.UtcNow);
        RosterEntryCreateRequest Request(DateOnly day) => new()
        {
            EmployeeId = w.Employee.Id.Value, RosterTypeId = absence.Id.Value, DateFrom = day, DateTo = day
        };

        await Refused(Grants.RosterEntriesWritePast, () => roster.Create(w.OrganizationId, w.ActorUserId, true, Request(today.AddDays(-1))));
        await roster.Create(w.OrganizationId, w.ActorUserId, true, Request(today));
        RosterEntryDto future = await roster.Create(w.OrganizationId, w.ActorUserId, true, Request(today.AddDays(3)));

        // Premještanje budućeg zapisa u prošlost je također upis u prošlost.
        await Assert.ThrowsAsync<ForbiddenAppException>(() => roster.Update(w.OrganizationId, w.ActorUserId, true, future.Id, new RosterEntryUpdateRequest
        {
            EmployeeId = w.Employee.Id.Value, RosterTypeId = absence.Id.Value, DateFrom = today.AddDays(-2), DateTo = today.AddDays(-2)
        }));

        await w.GrantUser(w.ActorUserId, Grants.RosterEntriesWritePast);
        RosterEntryDto past = await roster.Create(w.OrganizationId, w.ActorUserId, true, Request(today.AddDays(-1)));
        await roster.Delete(w.OrganizationId, w.ActorUserId, true, past.Id);
    }

    #endregion
}
