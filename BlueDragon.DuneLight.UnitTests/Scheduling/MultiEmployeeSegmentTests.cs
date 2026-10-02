using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1G — multi-employee segments: equal execution participants (no primary), explicit pricing source (automatic for
/// one employee, explicit for 2+), the employee price tier, repricing on pricing-source changes, independent per-employee
/// commission from the participation's final price, reversal of every employee's entry, the execution-history guard, the
/// legacy single-employee boundary, ownership, and the unchanged hard rules (overlap, availability, room people, resources).
/// </summary>
public class MultiEmployeeSegmentTests
{
    /// <summary>"Duo" service (default 50) with Company+Service = 55, Ana-specific = 60 (all companies), Marko-specific = 70
    /// (company); Ivana has no price. "Hundred" service (default 100, no list prices). The three employees have NO service
    /// assignments — they may perform every service (M1G eligibility rule).</summary>
    private sealed record Studio(ServiceEntity Duo, ServiceEntity Hundred, Employee Ana, Employee Marko, Employee Ivana);

    private static async Task<Studio> SetUp(SchedulingWorld w)
    {
        ServiceEntity duo = await w.AddService(60, 50m, name: "Duo");
        ServiceEntity hundred = await w.AddService(60, 100m, name: "Hundred");
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        await w.AddPriceListItem(duo, 55m, SchedulingWorld.PastDay, companyId: w.Company.Id);
        await w.AddPriceListItem(duo, 60m, SchedulingWorld.PastDay, employeeId: ana.Id);
        await w.AddPriceListItem(duo, 70m, SchedulingWorld.PastDay, companyId: w.Company.Id, employeeId: marko.Id);
        return new Studio(duo, hundred, ana, marko, ivana);
    }

    private static AppointmentSegmentCreateRequest Seg(
        ServiceEntity service, DateTimeOffset start, Employee[] employees, SegmentPricingMode? mode = null, Employee pricing = null,
        decimal? amount = null, params Client[] clients) => new()
    {
        ServiceId = service.Id.Value,
        PlannedStart = start,
        EmployeeIds = employees.Select(e => e.Id.Value).ToList(),
        PricingMode = mode,
        PricingEmployeeId = pricing?.Id,
        Participants = clients.Select(c => new AppointmentParticipantCreateRequest { ClientId = c.Id.Value, Amount = amount }).ToList()
    };

    private static Task<AppointmentDto> Create(SchedulingWorld w, AppointmentSegmentCreateRequest segment, bool fullScope = true, Guid? userId = null) =>
        w.Appointments.Create(w.OrganizationId, userId ?? w.ActorUserId, fullScope, new AppointmentCreateRequest
        {
            CompanyId = w.Company.Id.Value,
            Segments = new List<AppointmentSegmentCreateRequest> { segment }
        });

    private static BookingParticipationDto Only(AppointmentDto dto) => dto.Bookings.Single().Participations.Single();

    private static Task<BookingDto> SetParticipation(SchedulingWorld w, Guid participationId, BookingStatus status,
        Guid? clientPackageId = null, IBookingService bookings = null) =>
        (bookings ?? w.Bookings).SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId, new BookingSetStatusRequest
        {
            Status = status,
            ClientPackageId = clientPackageId
        });

    private static Task<AppointmentDto> ChangeEmployees(SchedulingWorld w, Guid segmentId, Employee[] employees,
        SegmentPricingMode? mode = null, Employee pricing = null, bool fullScope = true, Guid? userId = null) =>
        w.Appointments.ChangeSegmentEmployees(w.OrganizationId, userId ?? w.ActorUserId, fullScope, segmentId,
            new AppointmentSegmentEmployeesChangeRequest
            {
                EmployeeIds = employees.Select(e => e.Id.Value).ToList(), PricingMode = mode, PricingEmployeeId = pricing?.Id
            });

    private static Task<AppointmentDto> ChangePricing(SchedulingWorld w, Guid segmentId, SegmentPricingMode? mode, Employee pricing = null) =>
        w.Appointments.ChangeSegmentPricingSource(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentPricingSourceChangeRequest { PricingMode = mode, PricingEmployeeId = pricing?.Id });

    private static async Task<List<CommissionEntry>> EntriesOf(SchedulingWorld w, Guid participationId) =>
        (await w.LoadCommissionEntries()).Where(e => e.BookingSegmentParticipationId == participationId).ToList();

    private static async Task<ValidationAppException> ValidationCode(string code, Func<Task> action)
    {
        ValidationAppException ex = await SchedulingAssert.Validation(action);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    #region Pricing matrix

    [Fact]
    public async Task Pricing_OneEmployee_IsAutomaticallyThatEmployee_AndTheSnapshotRecordsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pricing_OneEmployee_IsAutomaticallyThatEmployee_AndTheSnapshotRecordsIt));
        Studio s = await SetUp(w);

        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana }, clients: w.Client));

        AppointmentSegmentDto segment = dto.Segments.Single();
        Assert.Equal(SegmentPricingMode.Employee, segment.PricingMode);
        Assert.Equal(s.Ana.Id, segment.PricingEmployeeId);
        BookingParticipationDto p = Only(dto);
        Assert.Equal(60m, p.Amount);
        Assert.Equal(PriceSource.EmployeeAllCompanies, p.BaseAmountSource);
        Assert.Equal(SegmentPricingMode.Employee, p.PricingMode);
        Assert.Equal(s.Ana.Id, p.PricingEmployeeId);
    }

    [Fact]
    public async Task Pricing_TwoEmployees_UseTheExplicitlySelectedEmployee_OrStandardSkipsEveryEmployeeTier()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pricing_TwoEmployees_UseTheExplicitlySelectedEmployee_OrStandardSkipsEveryEmployeeTier));
        Studio s = await SetUp(w);
        Employee[] both = { s.Ana, s.Marko };

        BookingParticipationDto ana = Only(await Create(w, Seg(s.Duo, SchedulingWorld.Future(9), both, SegmentPricingMode.Employee, s.Ana, clients: w.Client)));
        BookingParticipationDto marko = Only(await Create(w, Seg(s.Duo, SchedulingWorld.Future(11), both, SegmentPricingMode.Employee, s.Marko, clients: w.Client)));
        AppointmentDto standardDto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(13), both, SegmentPricingMode.Standard, clients: w.Client));
        BookingParticipationDto standard = Only(standardDto);

        Assert.Equal((60m, PriceSource.EmployeeAllCompanies), (ana.Amount, ana.BaseAmountSource.Value));
        Assert.Equal((70m, PriceSource.EmployeeCompanySpecific), (marko.Amount, marko.BaseAmountSource.Value));
        Assert.Equal((55m, PriceSource.CompanySpecific), (standard.Amount, standard.BaseAmountSource.Value));
        Assert.Equal(SegmentPricingMode.Standard, standard.PricingMode);
        Assert.Null(standard.PricingEmployeeId);
        // Both employees remain equal execution participants regardless of the pricing source.
        Assert.Equal(new[] { s.Ana.Id, s.Marko.Id }.OrderBy(x => x), standardDto.Segments.Single().Employees.Select(e => (Guid?)e.EmployeeId).OrderBy(x => x));
        Assert.Null(standardDto.EmployeeId); // legacy flat projection: never a fake single employee
    }

    [Fact]
    public async Task Pricing_SelectedEmployeeWithoutAPrice_FallsBackToTheNonEmployeeTiers()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pricing_SelectedEmployeeWithoutAPrice_FallsBackToTheNonEmployeeTiers));
        Studio s = await SetUp(w);

        BookingParticipationDto p = Only(await Create(w,
            Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Ivana }, SegmentPricingMode.Employee, s.Ivana, clients: w.Client)));

        Assert.Equal((55m, PriceSource.CompanySpecific), (p.Amount, p.BaseAmountSource.Value));
        Assert.Equal((SegmentPricingMode.Employee, s.Ivana.Id), (p.PricingMode.Value, p.PricingEmployeeId));
    }

    [Fact]
    public async Task Pricing_InvalidOrMissingSelections_AreRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Pricing_InvalidOrMissingSelections_AreRejected));
        Studio s = await SetUp(w);
        Employee[] both = { s.Ana, s.Marko };
        DateTimeOffset at = SchedulingWorld.Future(10);

        await ValidationCode(ErrorCodes.PricingSourceRequired, () => Create(w, Seg(s.Duo, at, both, clients: w.Client)));                               // F
        await ValidationCode(ErrorCodes.InvalidPricingSource, () => Create(w, Seg(s.Duo, at, both, SegmentPricingMode.Employee, s.Ivana, clients: w.Client))); // G
        await ValidationCode(ErrorCodes.InvalidPricingSource, () => Create(w, Seg(s.Duo, at, both, SegmentPricingMode.Standard, s.Ana, clients: w.Client)));  // H
        await ValidationCode(ErrorCodes.InvalidPricingSource, () => Create(w, Seg(s.Duo, at, both, SegmentPricingMode.Employee, clients: w.Client)));         // I
        await ValidationCode(ErrorCodes.InvalidPricingSource, () => Create(w, Seg(s.Duo, at, new[] { s.Ana }, SegmentPricingMode.Standard, clients: w.Client)));
        await SchedulingAssert.Validation(() => Create(w, Seg(s.Duo, at, new[] { s.Ana, s.Ana }, SegmentPricingMode.Standard, clients: w.Client))); // duplicate
        await SchedulingAssert.Validation(() => Create(w, Seg(s.Duo, at, Array.Empty<Employee>(), clients: w.Client))); // individual: still >= 1
        Assert.Equal(0, await w.CountAppointments());
    }

    #endregion

    #region Pricing-source mutations

    [Fact]
    public async Task EmployeeSet_AddingASecondEmployee_RequiresAnExplicitChoice_AndRepricesConfirmedParticipations()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeSet_AddingASecondEmployee_RequiresAnExplicitChoice_AndRepricesConfirmedParticipations));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana }, clients: w.Client));
        Guid segment = dto.Segments.Single().Id;

        await ValidationCode(ErrorCodes.PricingSourceRequired, () => ChangeEmployees(w, segment, new[] { s.Ana, s.Marko }));

        AppointmentDto changed = await ChangeEmployees(w, segment, new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Marko);
        Assert.Equal(2, changed.Segments.Single().Employees.Count);
        Assert.Equal(70m, Only(changed).Amount);
        Assert.Equal(s.Marko.Id, Only(changed).PricingEmployeeId);
    }

    [Fact]
    public async Task PricingSource_SwitchesWithoutChangingEmployees_AndRepricesEveryTime()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PricingSource_SwitchesWithoutChangingEmployees_AndRepricesEveryTime));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ana, clients: w.Client));
        Guid segment = dto.Segments.Single().Id;
        Assert.Equal(60m, Only(dto).Amount);

        AppointmentDto toMarko = await ChangePricing(w, segment, SegmentPricingMode.Employee, s.Marko);
        Assert.Equal(70m, Only(toMarko).Amount);
        AppointmentDto toStandard = await ChangePricing(w, segment, SegmentPricingMode.Standard);
        Assert.Equal((55m, SegmentPricingMode.Standard), (Only(toStandard).Amount, Only(toStandard).PricingMode.Value));
        Assert.Equal(2, toStandard.Segments.Single().Employees.Count);
    }

    [Fact]
    public async Task EmployeeSet_RemovingDownToOne_IsAutomatic_ButRemainingTwoPlusNeedsANewChoice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeSet_RemovingDownToOne_IsAutomatic_ButRemainingTwoPlusNeedsANewChoice));
        Studio s = await SetUp(w);
        AppointmentDto two = await Create(w, Seg(s.Duo, SchedulingWorld.Future(9), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ana, clients: w.Client));
        AppointmentDto three = await Create(w, Seg(s.Duo, SchedulingWorld.Future(12), new[] { s.Ana, s.Marko, s.Ivana }, SegmentPricingMode.Employee, s.Ana, clients: w.Client));

        AppointmentDto marko = await ChangeEmployees(w, two.Segments.Single().Id, new[] { s.Marko });
        Assert.Equal((SegmentPricingMode.Employee, s.Marko.Id), (marko.Segments.Single().PricingMode, marko.Segments.Single().PricingEmployeeId));
        Assert.Equal(70m, Only(marko).Amount);

        // Even though Ana (the old pricing employee) is removed and 2 remain, the caller must choose again.
        await ValidationCode(ErrorCodes.PricingSourceRequired, () => ChangeEmployees(w, three.Segments.Single().Id, new[] { s.Marko, s.Ivana }));
        // Keeping Ana while removing Ivana still requires an explicit confirmation for 2+.
        await ValidationCode(ErrorCodes.PricingSourceRequired, () => ChangeEmployees(w, three.Segments.Single().Id, new[] { s.Ana, s.Marko }));
    }

    [Fact]
    public async Task PricingSource_OfASingleEmployeeSegment_CannotBeStandard()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PricingSource_OfASingleEmployeeSegment_CannotBeStandard));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana }, clients: w.Client));

        await ValidationCode(ErrorCodes.InvalidPricingSource, () => ChangePricing(w, dto.Segments.Single().Id, SegmentPricingMode.Standard));
        await ValidationCode(ErrorCodes.PricingSourceRequired, () => ChangePricing(w, dto.Segments.Single().Id, null));
    }

    [Fact]
    public async Task ManualFinalPrice_IsPreservedOnRepricing_WhileTheSuggestedSnapshotStaysTruthful()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualFinalPrice_IsPreservedOnRepricing_WhileTheSuggestedSnapshotStaysTruthful));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ana, 45m, w.Client));

        BookingParticipationDto p = Only(await ChangePricing(w, dto.Segments.Single().Id, SegmentPricingMode.Employee, s.Marko));

        Assert.Equal(45m, p.Amount);
        Assert.Equal(70m, p.SuggestedAmount);
        Assert.True(p.IsAmountManuallyOverridden);
        Assert.Equal((70m, s.Marko.Id), (p.BaseAmount.Value, p.PricingEmployeeId));
    }

    [Fact]
    public async Task HistoricalPricingSnapshot_IsNotRewritten_ByALaterPricingSourceChange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(HistoricalPricingSnapshot_IsNotRewritten_ByALaterPricingSourceChange));
        Studio s = await SetUp(w);
        Client other = await w.AddClient("Other");
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ana, null, w.Client, other));
        Guid done = dto.Bookings.Single(b => b.ClientId == w.Client.Id).Participations.Single().Id;
        await SetParticipation(w, done, BookingStatus.Completed);

        AppointmentDto after = await ChangePricing(w, dto.Segments.Single().Id, SegmentPricingMode.Employee, s.Marko);

        BookingParticipationDto completed = after.Bookings.Single(b => b.ClientId == w.Client.Id).Participations.Single();
        BookingParticipationDto confirmed = after.Bookings.Single(b => b.ClientId == other.Id).Participations.Single();
        Assert.Equal((60m, s.Ana.Id), (completed.Amount, completed.PricingEmployeeId)); // history keeps Ana
        Assert.Equal((70m, s.Marko.Id), (confirmed.Amount, confirmed.PricingEmployeeId));
    }

    #endregion

    #region Commission

    private static async Task<(AppointmentDto Dto, Guid ParticipationId)> CreateHundred(
        SchedulingWorld w, Studio s, Employee[] employees, DateTimeOffset? at = null, decimal? amount = null) =>
        await CreateAndPick(w, Seg(s.Hundred, at ?? SchedulingWorld.Future(10), employees,
            employees.Length > 1 ? SegmentPricingMode.Standard : null, null, amount, w.Client));

    private static async Task<(AppointmentDto Dto, Guid ParticipationId)> CreateAndPick(SchedulingWorld w, AppointmentSegmentCreateRequest seg)
    {
        AppointmentDto dto = await Create(w, seg);
        return (dto, Only(dto).Id);
    }

    [Fact]
    public async Task Commission_EveryEmployeeEarnsIndependently_FromTheFinalPrice_NoSplitting()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_EveryEmployeeEarnsIndependently_FromTheFinalPrice_NoSplitting));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Hundred, CommissionCalculationType.Percentage, 15m);
        (_, Guid participation) = await CreateHundred(w, s, new[] { s.Ana, s.Marko, s.Ivana }); // Ivana has no rule

        await SetParticipation(w, participation, BookingStatus.Completed);

        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(2, entries.Count);
        Assert.Equal(10m, entries.Single(e => e.EmployeeId == s.Ana.Id).CommissionAmount);
        Assert.Equal(15m, entries.Single(e => e.EmployeeId == s.Marko.Id).CommissionAmount);
        Assert.All(entries, e => Assert.Equal(100m, e.BaseAmount));
        Assert.Equal(25m, entries.Sum(e => e.CommissionAmount));
        Assert.DoesNotContain(entries, e => e.EmployeeId == s.Ivana.Id);
    }

    [Fact]
    public async Task Commission_Fixed_IsTheConfiguredAmountPerEmployee_RegardlessOfThePrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_Fixed_IsTheConfiguredAmountPerEmployee_RegardlessOfThePrice));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Fixed, 12m);
        await w.AddCommissionRule(s.Marko, s.Hundred, CommissionCalculationType.Fixed, 20m);
        (_, Guid participation) = await CreateHundred(w, s, new[] { s.Ana, s.Marko }, amount: 37m);

        await SetParticipation(w, participation, BookingStatus.Completed);

        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(12m, entries.Single(e => e.EmployeeId == s.Ana.Id).CommissionAmount);
        Assert.Equal(20m, entries.Single(e => e.EmployeeId == s.Marko.Id).CommissionAmount);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(70)]
    public async Task Commission_Percentage_UsesTheFinalPriceAfterManualAdjustment_NotTheSuggestedPrice(int finalPrice)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_Percentage_UsesTheFinalPriceAfterManualAdjustment_NotTheSuggestedPrice) + finalPrice);
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        (AppointmentDto dto, Guid participation) = await CreateHundred(w, s, new[] { s.Ana }, amount: finalPrice);
        Assert.Equal(100m, Only(dto).SuggestedAmount);

        await SetParticipation(w, participation, BookingStatus.Completed);

        CommissionEntry entry = Assert.Single(await EntriesOf(w, participation));
        Assert.Equal(finalPrice, entry.BaseAmount);
        Assert.Equal(finalPrice / 10m, entry.CommissionAmount);
    }

    [Fact]
    public async Task Commission_OfAPackageCoveredParticipation_StillUsesTheFinalPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_OfAPackageCoveredParticipation_StillUsesTheFinalPrice));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Hundred, CommissionCalculationType.Percentage, 15m);
        ClientPackage package = await w.AddClientPackage(w.Client, s.Hundred, 5, SchedulingWorld.FutureDay.AddDays(30));
        (_, Guid participation) = await CreateHundred(w, s, new[] { s.Ana, s.Marko });

        BookingDto booking = await SetParticipation(w, participation, BookingStatus.Completed, clientPackageId: package.Id);

        Assert.True(booking.Participations.Single().PackageCovered);
        Assert.Equal(100m, booking.Participations.Single().Amount); // coverage does not zero the final price
        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(new[] { 10m, 15m }, entries.OrderBy(e => e.CommissionAmount).Select(e => e.CommissionAmount));
    }

    [Fact]
    public async Task Commission_WithPartialPayment_IsBasedOnTheFinalPrice_NotOnMoneyPaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_WithPartialPayment_IsBasedOnTheFinalPrice_NotOnMoneyPaid));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        (AppointmentDto dto, Guid participation) = await CreateHundred(w, s, new[] { s.Ana });
        await w.PayBookingViaCheckout(dto.Bookings.Single().Id, w.Client, 30m);

        BookingDto booking = await SetParticipation(w, participation, BookingStatus.Completed);

        Assert.Equal(70m, booking.Participations.Single().OutstandingAmount);
        Assert.Equal(10m, Assert.Single(await EntriesOf(w, participation)).CommissionAmount);
    }

    [Fact]
    public async Task Commission_PricingEmployeeIsNotTheBeneficiary_BothEmployeesEarnOnTheAnaPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_PricingEmployeeIsNotTheBeneficiary_BothEmployeesEarnOnTheAnaPrice));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Duo, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Duo, CommissionCalculationType.Percentage, 15m);
        (AppointmentDto dto, Guid participation) = await CreateAndPick(w,
            Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ana, null, w.Client));
        Assert.Equal(60m, Only(dto).Amount);

        await SetParticipation(w, participation, BookingStatus.Completed);

        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(6m, entries.Single(e => e.EmployeeId == s.Ana.Id).CommissionAmount);
        Assert.Equal(9m, entries.Single(e => e.EmployeeId == s.Marko.Id).CommissionAmount); // Marko earns without being the pricing employee
    }

    [Fact]
    public async Task Commission_StandardPricing_DoesNotFilterBeneficiaries()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_StandardPricing_DoesNotFilterBeneficiaries));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Duo, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Duo, CommissionCalculationType.Percentage, 20m);
        (AppointmentDto dto, Guid participation) = await CreateAndPick(w,
            Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, null, null, w.Client));
        Assert.Equal(55m, Only(dto).Amount);

        await SetParticipation(w, participation, BookingStatus.Completed);

        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(5.5m, entries.Single(e => e.EmployeeId == s.Ana.Id).CommissionAmount);
        Assert.Equal(11m, entries.Single(e => e.EmployeeId == s.Marko.Id).CommissionAmount);
    }

    [Fact]
    public async Task Commission_Correction_ReversesEveryEmployeesEntry_AndReCompletionEarnsOneFreshEntryEach()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_Correction_ReversesEveryEmployeesEntry_AndReCompletionEarnsOneFreshEntryEach));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Hundred, CommissionCalculationType.Percentage, 15m);
        (_, Guid participation) = await CreateHundred(w, s, new[] { s.Ana, s.Marko });

        await SetParticipation(w, participation, BookingStatus.Completed);
        await SetParticipation(w, participation, BookingStatus.Confirmed);

        List<CommissionEntry> reversed = await EntriesOf(w, participation);
        Assert.Equal(2, reversed.Count);
        Assert.All(reversed, e => Assert.Equal(CommissionEntryStatus.Reversed, e.Status));

        await SetParticipation(w, participation, BookingStatus.Completed);

        List<CommissionEntry> all = await EntriesOf(w, participation);
        List<CommissionEntry> earned = all.Where(e => e.Status == CommissionEntryStatus.Earned).ToList();
        Assert.Equal(4, all.Count);
        Assert.Equal(new[] { s.Ana.Id.Value, s.Marko.Id.Value }.OrderBy(x => x), earned.Select(e => e.EmployeeId).OrderBy(x => x));
        Assert.All(earned, e => Assert.True(e.SourceVersion > reversed[0].SourceVersion));
    }

    [Fact]
    public async Task Commission_ConcurrentCompletionRequests_EarnExactlyOneEntryPerEmployee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Commission_ConcurrentCompletionRequests_EarnExactlyOneEntryPerEmployee));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Hundred, CommissionCalculationType.Fixed, 5m);
        (_, Guid participation) = await CreateHundred(w, s, new[] { s.Ana, s.Marko });

        async Task<Exception> Complete()
        {
            using IServiceScope scope = SchedulingTestHost.CreateScope();
            try
            {
                await SetParticipation(w, participation, BookingStatus.Completed, bookings: scope.ServiceProvider.GetRequiredService<IBookingService>());
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        Exception[] outcomes = await Task.WhenAll(Task.Run(Complete), Task.Run(Complete), Task.Run(Complete));

        Assert.Contains(outcomes, o => o == null);
        Assert.All(outcomes.Where(o => o != null), o => Assert.IsType<BusinessRuleException>(o));
        List<CommissionEntry> entries = await EntriesOf(w, participation);
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(e => e.EmployeeId).Distinct().Count());
    }

    #endregion

    #region History, legacy boundary, ownership

    [Fact]
    public async Task EmployeeSet_OfASegmentWithCompletedWork_IsLocked_AndCommissionStaysWithThePerformers()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EmployeeSet_OfASegmentWithCompletedWork_IsLocked_AndCommissionStaysWithThePerformers));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Hundred, CommissionCalculationType.Percentage, 10m);
        (AppointmentDto dto, Guid participation) = await CreateHundred(w, s, new[] { s.Ana });
        await SetParticipation(w, participation, BookingStatus.Completed);

        await SchedulingAssert.BusinessRule(ErrorCodes.SegmentExecutionHistoryLocked,
            () => ChangeEmployees(w, dto.Segments.Single().Id, new[] { s.Marko }));

        Assert.Equal(s.Ana.Id, Assert.Single((await w.Appointments.GetById(w.OrganizationId, dto.Id)).Segments.Single().Employees).EmployeeId);
        Assert.Equal(s.Ana.Id, Assert.Single(await EntriesOf(w, participation)).EmployeeId);
    }

    [Fact]
    public async Task LegacySingleEmployeeOperations_NeverCollapseAMultiEmployeeSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(LegacySingleEmployeeOperations_NeverCollapseAMultiEmployeeSegment));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: w.Client));

        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeSetCommandRequired, () => w.Appointments.Move(
            w.OrganizationId, w.ActorUserId, true, dto.Id, new AppointmentMoveRequest { StartsAt = SchedulingWorld.Future(12) }));

        Assert.Equal(2, (await w.Appointments.GetById(w.OrganizationId, dto.Id)).Segments.Single().Employees.Count);
    }

    [Fact]
    public async Task Ownership_EveryAssignedEmployeeOwnsTheSegment_OthersDoNot_AndRosterChangesNeedWriteAll()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Ownership_EveryAssignedEmployeeOwnsTheSegment_OthersDoNot_AndRosterChangesNeedWriteAll));
        Studio s = await SetUp(w);
        AppointmentDto dto = await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: w.Client));
        Guid segment = dto.Segments.Single().Id;

        Task<AppointmentDto> Move(Employee caller, int hour) => w.Appointments.ChangeSegmentTime(w.OrganizationId, caller.UserId, false, segment,
            new AppointmentSegmentTimeChangeRequest { PlannedStart = SchedulingWorld.Future(hour) });

        await Move(s.Ana, 11);
        await Move(s.Marko, 12);
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => Move(s.Ivana, 13));
        // Own scope cannot remove (or add) coworkers — even an assigned employee.
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => ChangeEmployees(w, segment, new[] { s.Ana }, fullScope: false, userId: s.Ana.UserId));
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => w.Appointments.Cancel(w.OrganizationId, s.Ana.UserId, false, dto.Id,
            new AppointmentCancelRequest { CancellationReason = "own" }));
        // Own scope cannot create a segment for a coworker either.
        await SchedulingAssert.BusinessRule(ErrorCodes.NotOwner, () => Create(w,
            Seg(s.Duo, SchedulingWorld.Future(15), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: w.Client), fullScope: false, userId: s.Ana.UserId));
    }

    #endregion

    #region Hard rules per employee

    [Fact]
    public async Task HardRules_ApplyToEveryEmployee_OverlapAndAvailability()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(HardRules_ApplyToEveryEmployee_OverlapAndAvailability));
        Studio s = await SetUp(w);
        Client other = await w.AddClient("Other");
        await Create(w, Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Marko }, clients: other));
        await w.AddAbsence(s.Ivana, SchedulingWorld.FutureDay);

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap, () => Create(w,
            Seg(s.Duo, SchedulingWorld.Future(10, 30), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: w.Client)));
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent, () => Create(w,
            Seg(s.Duo, SchedulingWorld.Future(14), new[] { s.Ana, s.Ivana }, SegmentPricingMode.Standard, clients: w.Client)));
        // Adjacent intervals stay valid; different employees may share one segment.
        await Create(w, Seg(s.Duo, SchedulingWorld.Future(11), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: w.Client));
    }

    [Fact]
    public async Task RoomPeople_CountEveryEmployee_WhileResourceQuantityIsPerSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RoomPeople_CountEveryEmployee_WhileResourceQuantityIsPerSegment));
        Studio s = await SetUp(w);
        Room room = await w.AddRoom(capacity: 4);
        Resource table = await w.AddResource(capacity: 1);
        Client second = await w.AddClient("Second");
        Client third = await w.AddClient("Third");
        AppointmentSegmentCreateRequest seg = Seg(s.Duo, SchedulingWorld.Future(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, null, null, w.Client, second);
        seg.RoomId = room.Id;
        seg.Resources = new List<AppointmentSegmentResourceRequest> { new() { ResourceId = table.Id.Value, QuantityRequired = 1 } };

        AppointmentDto dto = await Create(w, seg); // 2 employees + 2 clients = 4 people; resource 1 of 1 (not ×2)

        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => w.Appointments.AddClient(w.OrganizationId, w.ActorUserId, true, dto.Id,
            new AppointmentClientAddRequest
            {
                ClientId = third.Id.Value,
                Participations = new List<AppointmentClientParticipationRequest> { new() { SegmentId = dto.Segments.Single().Id } }
            }));
    }

    #endregion

    #region Employee price tier and eligibility

    [Fact]
    public async Task PriceList_EmployeeIsPartOfTheOverlapScope_AndOnlyServicesHaveEmployeePrices()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceList_EmployeeIsPartOfTheOverlapScope_AndOnlyServicesHaveEmployeePrices));
        Studio s = await SetUp(w);
        IPricingService pricing = w.Resolve<IPricingService>();
        PriceListItemCreateRequest Item(Employee employee, Guid? companyId = null) => new()
        {
            SubjectType = PricingSubjectType.Service, ServiceId = s.Hundred.Id, CompanyId = companyId, EmployeeId = employee?.Id,
            Price = 90m, ValidFrom = SchedulingWorld.PastDay
        };

        await pricing.Create(w.OrganizationId, w.ActorUserId, Item(null));
        await pricing.Create(w.OrganizationId, w.ActorUserId, Item(s.Ana));   // different employee scope → no overlap
        await pricing.Create(w.OrganizationId, w.ActorUserId, Item(s.Marko));
        await SchedulingAssert.BusinessRule(ErrorCodes.PriceOverlap, () => pricing.Create(w.OrganizationId, w.ActorUserId, Item(s.Ana)));
        await SchedulingAssert.Validation(() => pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Package, PackageId = Guid.NewGuid(), EmployeeId = s.Ana.Id, Price = 1m, ValidFrom = SchedulingWorld.PastDay
        }));

        ResolvePriceResponse standard = await pricing.ResolvePrice(w.OrganizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service, SubjectId = s.Duo.Id.Value, CompanyId = w.Company.Id, Date = SchedulingWorld.FutureDay
        });
        Assert.Equal((55m, PriceSource.CompanySpecific), (standard.Price, standard.Source));
        // The effective (standard) price list never shows an employee price.
        List<EffectivePriceDto> effective = await pricing.GetEffectivePriceList(w.OrganizationId, w.Company.Id, SchedulingWorld.FutureDay);
        Assert.Equal(55m, effective.Single(e => e.SubjectId == s.Duo.Id).Price);
    }

    [Fact]
    public async Task Eligibility_NoAssignmentsMeansEveryService_AnyAssignmentRestricts_ForEveryEmployeeOfTheSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Eligibility_NoAssignmentsMeansEveryService_AnyAssignmentRestricts_ForEveryEmployeeOfTheSegment));
        Studio s = await SetUp(w);
        Employee restricted = await w.AddEmployeeRestrictedToAnotherService("Restricted");

        await Create(w, Seg(s.Hundred, SchedulingWorld.Future(9), new[] { s.Ana }, clients: w.Client)); // no assignments → allowed
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeNotAssignedToService, () => Create(w,
            Seg(s.Hundred, SchedulingWorld.Future(11), new[] { s.Ana, restricted }, SegmentPricingMode.Standard, clients: w.Client)));
    }

    #endregion
}
