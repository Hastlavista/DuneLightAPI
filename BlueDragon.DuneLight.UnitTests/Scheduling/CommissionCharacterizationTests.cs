#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix R): commission generation and reversal for scheduling.
///
/// INDIVIDUAL service commission belongs to a BOOKING: one Earned CommissionEntry per completed Booking, employee taken
/// from the appointment at the moment of completion, base = Booking.Amount (the retail amount — even for package-covered
/// bookings), identity = (BookingId, SourceVersion = Booking.StatusVersion). A correction (Completed → Confirmed) flips
/// the entry to Reversed; a later re-completion creates a NEW entry at the new version.
///
/// GROUP service commission belongs to the OCCURRENCE: one entry per completed group appointment (not per attendee), Fixed
/// rules only, no booking link, base 0, created by CompleteGroupAppointment, and NEVER reversed (booking-level check-in
/// corrections do not touch it). This is the Individual/Group asymmetry the redesign must consciously decide on.
/// </summary>
public class CommissionCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    #region Individual — generation

    [Fact]
    public async Task Individual_PercentageRule_EarnsAPercentageOfTheBookingAmount_IncludingAManualOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_PercentageRule_EarnsAPercentageOfTheBookingAmount_IncludingAManualOverride));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlementAmount: 40m));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(40m, entry.BaseAmount); // the price actually charged, not the list price
        Assert.Equal(4m, entry.CommissionAmount);
        Assert.Equal(CommissionCalculationType.Percentage, entry.CalculationType);
        Assert.Equal(10m, entry.RuleValue);
    }

    [Fact]
    public async Task Individual_FixedRule_EarnsTheFixedAmountPerBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_FixedRule_EarnsTheFixedAmountPerBooking));
        Client partner = await w.AddClient("Partner", "Client");
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 7m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
        }));

        // Two clients on one appointment = two commissionable services (one entry per Booking).
        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal(7m, e.CommissionAmount));
        Assert.Equal(2, entries.Select(e => e.BookingId).Distinct().Count());
    }

    [Fact]
    public async Task Individual_TheEmployeeIsTheOneOnTheAppointmentAtCompletion_NotTheOneItWasBookedWith()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_TheEmployeeIsTheOneOnTheAppointmentAtCompletion_NotTheOneItWasBookedWith));
        Employee substitute = await w.AddEmployee("Substitute");
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        await w.AddCommissionRule(substitute, w.Service, CommissionCalculationType.Fixed, 9m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        // M1H: the substitute is assigned through the segment command, then the participation is completed.
        await w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, created.Segments[0].Id,
            new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { substitute.Id.Value } });

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(substitute.Id, entry.EmployeeId);
        Assert.Equal(9m, entry.CommissionAmount);
    }

    [Fact]
    public async Task Individual_NoRuleForThisEmployeeAndService_MeansNoCommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_NoRuleForThisEmployeeAndService_MeansNoCommission));
        ServiceEntityAlias otherService = await w.AddService(30, 10m);
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.AddCommissionRule(w.Employee, otherService, CommissionCalculationType.Fixed, 5m);   // right employee, wrong service
        await w.AddCommissionRule(otherEmployee, w.Service, CommissionCalculationType.Fixed, 5m);   // right service, wrong employee

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Individual_AnInactiveRule_MeansNoCommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_AnInactiveRule_MeansNoCommission));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        await w.DeactivateCommissionRules();

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Individual_CancelAndNoShow_NeverEarnACommission()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_CancelAndNoShow_NeverEarnACommission));
        Client noShow = await w.AddClient("NoShow", "Client");
        Employee otherEmployee = await w.AddEmployee("Other");
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        await w.AddCommissionRule(otherEmployee, w.Service, CommissionCalculationType.Fixed, 5m);
        AppointmentDto toCancel = await w.CreateAppointment(SchedulingWorld.Future(10));
        AppointmentDto toNoShow = await w.CreateAppointment(SchedulingWorld.Future(10), client: noShow, employee: otherEmployee);

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, toCancel.Id, new AppointmentCancelRequest());
        await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, toNoShow.Id, new AppointmentCancelRequest());

        Assert.Empty(await w.LoadCommissionEntries());
    }

    #endregion

    #region Individual — reversal and re-completion

    [Fact]
    public async Task Individual_ACorrectionReversesTheEntry_AndAReCompletionEarnsANewOneAtTheNewVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ACorrectionReversesTheEntry_AndAReCompletionEarnsANewOneAtTheNewVersion));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);
        Assert.Equal(CommissionEntryStatus.Reversed, Assert.Single(await w.LoadCommissionEntries()).Status);

        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlementAmount: 80m));

        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(2, entries.Count);
        Assert.Equal(CommissionEntryStatus.Reversed, entries.Single(e => e.SourceVersion == 1).Status);
        CommissionEntry live = entries.Single(e => e.SourceVersion == 3);
        Assert.Equal(CommissionEntryStatus.Earned, live.Status);
        Assert.Equal(80m, live.BaseAmount); // re-earned on the NEW price
        Assert.Equal(8m, live.CommissionAmount);
    }

    [Fact]
    public async Task Individual_ACorrectionWithoutARule_ReversesNothing_AndIsNotAnError()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ACorrectionWithoutARule_ReversesNothing_AndIsNotAnError));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        BookingDto dto = await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Assert.Equal(BookingStatusSummary.Confirmed, dto.Status);
        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Individual_TheReversalIsScopedToTheCorrectedBooking_ASiblingsEntryStaysEarned()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_TheReversalIsScopedToTheCorrectedBooking_ASiblingsEntryStaysEarned));
        Client partner = await w.AddClient("Partner", "Client");
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
        }));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        Guid correctedBooking = (await w.LoadBooking(created.Id, w.Client)).Id.Value;
        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(CommissionEntryStatus.Reversed, entries.Single(e => e.BookingId == correctedBooking).Status);
        Assert.Equal(CommissionEntryStatus.Earned, entries.Single(e => e.BookingId != correctedBooking).Status);
    }

    [Fact]
    public async Task Individual_ReCompletingASiblingDoesNotDuplicateTheAlreadyEarnedEntryOfTheOtherBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ReCompletingASiblingDoesNotDuplicateTheAlreadyEarnedEntryOfTheOtherBooking));
        Client partner = await w.AddClient("Partner", "Client");
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Fixed, 5m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: partner);
        TestCompletionSpec both = w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
        });
        await w.CompleteParticipations(created.Id, both);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        // The client list is re-sent in full (so the still-Completed sibling is not deleted as "omitted").
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), settlements: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value },
            new AppointmentCompletedClientRequest { ClientId = partner.Id.Value }
        }));

        // Only the booking that actually transitioned in this call earns again: 2 (first pass) + 1 (re-completion) = 3.
        Assert.Equal(3, (await w.LoadCommissionEntries()).Count);
        Assert.Equal(2, (await w.LoadCommissionEntries()).Count(e => e.Status == CommissionEntryStatus.Earned));
    }

    #endregion

    #region Group

    [Fact]
    public async Task Group_CompletingTheOccurrence_EarnsExactlyOneFixedEntry_RegardlessOfTheNumberOfAttendees()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CompletingTheOccurrence_EarnsExactlyOneFixedEntry_RegardlessOfTheNumberOfAttendees));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, capacity: 6);
        for (int i = 0; i < 3; i++)
            await w.AddGroupMember(group, i == 0 ? w.Client : await w.AddClient($"M{i}", "Client"));
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal(CommissionSourceType.GroupService, entry.SourceType);
        Assert.Equal(20m, entry.CommissionAmount);
        Assert.Equal(0m, entry.BaseAmount);
        Assert.Null(entry.BookingId);
        Assert.Equal(occurrence.Id, entry.AppointmentId);
        Assert.Equal(w.Employee.Id, entry.EmployeeId);
        Assert.Equal(CommissionEntryStatus.Earned, entry.Status);
        Assert.Equal(0, entry.SourceVersion);
    }

    [Fact]
    public async Task Group_APercentageRule_EarnsNothing_BecauseTheOccurrenceHasNoPriceOfItsOwn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_APercentageRule_EarnsNothing_BecauseTheOccurrenceHasNoPriceOfItsOwn));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Percentage, 10m);
        var group = await w.CreateGroup(svc, capacity: 6);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Group_ATrainerlessOccurrence_EarnsNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_ATrainerlessOccurrence_EarnsNothing));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, capacity: 6, withTrainer: false);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        // Full scope is required to operate a trainerless occurrence (there is no owner to compare against).
        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Group_BookingLevelCheckIn_NeverEarnsACommission_OnlyClosingTheOccurrenceDoes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_BookingLevelCheckIn_NeverEarnsACommission_OnlyClosingTheOccurrenceDoes));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, capacity: 6);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task Group_AnOccurrenceCompletedWithZeroAttendees_StillEarnsTheFixedEntry()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_AnOccurrenceCompletedWithZeroAttendees_StillEarnsTheFixedEntry));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, capacity: 6);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.NoShow); // nobody actually attended

        await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        // FINDING: the occurrence commission depends on the frame being Completed, not on anyone having attended.
        Assert.Equal(20m, Assert.Single(await w.LoadCommissionEntries()).CommissionAmount);
    }

    [Fact]
    public async Task Group_CancellingTheWholeOccurrence_EarnsNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CancellingTheWholeOccurrence_EarnsNothing));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddCommissionRule(w.Employee, svc, CommissionCalculationType.Fixed, 20m);
        var group = await w.CreateGroup(svc, capacity: 6);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new AppointmentCancelRequest());

        Assert.Empty(await w.LoadCommissionEntries());
    }

    #endregion
}
