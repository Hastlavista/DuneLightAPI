#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-8 — pravila cijena: cijena se zamrzava po sudjelovanju pri upisu (grupna prisutnost i ručni iznos pri odrađivanju ne čitaju
/// cjenik ponovno); segmentne naredbe čitaju cjenik samo kad se promijeni nešto o čemu cijena ovisi i javljaju promjenu cijene;
/// audit ručnog iznosa; ručni iznos i provizija; preklapanje stavki navodi stavku; rupa u cjeniku (PRICE_NOT_DEFINED); upozorenja
/// spremanja cjenika; izvještaj provizija s ponovno zarađenom provizijom.
/// </summary>
public class T1PricingRulesTests
{
    private static DateOnly FutureDate => SchedulingWorld.Day(SchedulingWorld.FutureDay);

    private static async Task<List<BookingSegmentParticipation>> ParticipationsOf(SchedulingWorld w, Guid appointmentId)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.BookingSegmentParticipations.Include(p => p.Booking)
            .Where(p => p.Booking.AppointmentId == appointmentId).ToListAsync();
    }

    private static async Task<BookingSegmentParticipation> OnlyParticipation(SchedulingWorld w, Guid appointmentId, Client client) =>
        (await ParticipationsOf(w, appointmentId)).Single(p => p.Booking.ClientId == client.Id);

    private static T Details<T>(WarningDto warning) => Assert.IsType<T>(warning.Details);

    private static decimal Amount(string auditValue) => decimal.Parse(auditValue, CultureInfo.InvariantCulture);

    #region (1) Grupna prisutnost koristi cijenu iz upisa

    [Fact]
    public async Task GroupAttendance_UsesThePriceStoredAtEnrolment_ALaterEnrolmentGetsThePriceValidAtItsEnrolment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupAttendance_UsesThePriceStoredAtEnrolment_ALaterEnrolmentGetsThePriceValidAtItsEnrolment));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group); // stalni polaznik: cijena pri generiranju (15)

        // Cjenik se mijenja između upisa i prisutnosti.
        await w.AddPriceListItem(groupService, 25m, FutureDate);
        Client guest = await w.AddClient("Guest", "Late");
        await w.AddGuest(occurrence, guest); // kasniji upis: cijena važeća za dan termina u trenutku upisa (25)

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed, isPaid: false);
        await w.SetBookingStatus(occurrence.Id.Value, guest, BookingStatus.Completed, isPaid: false);

        BookingSegmentParticipation member = await OnlyParticipation(w, occurrence.Id.Value, w.Client);
        Assert.Equal((ParticipationStatus.Completed, 15m, 15m, 15m, PriceSource.Default),
            (member.Status, member.Amount, member.SuggestedAmount, member.BaseAmount.Value, member.BaseAmountSource.Value));
        BookingSegmentParticipation late = await OnlyParticipation(w, occurrence.Id.Value, guest);
        Assert.Equal((ParticipationStatus.Completed, 25m, 25m, PriceSource.AllCompanies),
            (late.Status, late.Amount, late.BaseAmount.Value, late.BaseAmountSource.Value));
    }

    [Fact]
    public async Task GroupAttendance_WithAManualAmount_KeepsTheStoredSuggestedPriceAndBase_AndIsAudited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupAttendance_WithAManualAmount_KeepsTheStoredSuggestedPriceAndBase_AndIsAudited));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.AddPriceListItem(groupService, 25m, FutureDate);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed, amount: 10m, isPaid: false);

        BookingSegmentParticipation member = await OnlyParticipation(w, occurrence.Id.Value, w.Client);
        Assert.Equal((10m, 15m, 15m, true), (member.Amount, member.SuggestedAmount, member.BaseAmount.Value, member.IsAmountManuallyOverridden));
        AppointmentAuditLog manual = Assert.Single(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "ManualAmount");
        Assert.Equal((15m, 10m, w.ActorUserId), (Amount(manual.OldValue), Amount(manual.NewValue), manual.ChangedBy.Value));
    }

    #endregion

    #region (2) + (4) Odrađivanje s ručnim iznosom zadržava spremljenu osnovicu; audit

    [Fact]
    public async Task IndividualCompletion_WithAManualAmount_KeepsTheStoredSuggestedPriceAndBase_AndAuditsSuggestedAndEntered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(IndividualCompletion_WithAManualAmount_KeepsTheStoredSuggestedPriceAndBase_AndAuditsSuggestedAndEntered));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10)); // 50 (zadana cijena)
        await w.AddPriceListItem(w.Service, 60m, FutureDate);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, amount: 40m, paymentMethod: PaymentMethod.Cash);

        BookingSegmentParticipation p = await OnlyParticipation(w, created.Id, w.Client);
        // CHANGED in T1 (odluka "Odrađivanje s ručnim iznosom"): prije se predložena cijena i osnovica osvježavale iz cjenika (60).
        Assert.Equal((40m, 50m, 50m, PriceSource.Default, true),
            (p.Amount, p.SuggestedAmount, p.BaseAmount.Value, p.BaseAmountSource.Value, p.IsAmountManuallyOverridden));

        List<AppointmentAuditLog> audit = await w.LoadAuditLog(created.Id);
        AppointmentAuditLog amount = Assert.Single(audit, l => l.ChangeType == "Amount");
        Assert.Equal((50m, 40m), (Amount(amount.OldValue), Amount(amount.NewValue)));
        AppointmentAuditLog manual = Assert.Single(audit, l => l.ChangeType == "ManualAmount");
        Assert.Equal((50m, 40m, w.ActorUserId, p.Id), (Amount(manual.OldValue), Amount(manual.NewValue), manual.ChangedBy.Value, manual.BookingSegmentParticipationId));
    }

    [Fact]
    public async Task SetParticipationPrice_AuditsTheSuggestedPriceNextToTheEnteredAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SetParticipationPrice_AuditsTheSuggestedPriceNextToTheEnteredAmount));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid participationId = await w.ParticipationIdOnOnlySegment(created.Id, w.Client.Id.Value);

        await w.Bookings.SetParticipationPrice(w.OrganizationId, w.ActorUserId, true, participationId, new ParticipationPriceChangeRequest { Amount = 35m });

        AppointmentAuditLog manual = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "ManualAmount");
        Assert.Equal((50m, 35m, w.ActorUserId), (Amount(manual.OldValue), Amount(manual.NewValue), manual.ChangedBy.Value));
        Assert.True(manual.ChangedAt > DateTimeOffset.MinValue);
    }

    #endregion

    #region (3) Segmentne naredbe čitaju cjenik samo kad se promijeni nešto o čemu cijena ovisi

    private static WarningParticipationPriceChangedDetails PriceChanged(AppointmentDto dto) =>
        Details<WarningParticipationPriceChangedDetails>(Assert.Single(dto.Warnings, x => x.Code == WarningCodes.ParticipationPriceChanged));

    [Fact]
    public async Task ChangeSegmentTime_WithinTheSameDay_KeepsThePrice_ToAnotherDay_RereadsThePriceListAndReportsTheChange()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentTime_WithinTheSameDay_KeepsThePrice_ToAnotherDay_RereadsThePriceListAndReportsTheChange));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.AddPriceListItem(w.Service, 60m, FutureDate);
        Guid participationId = await w.ParticipationIdOnOnlySegment(created.Id, w.Client.Id.Value);

        // CHANGED in T1: pomak unutar istog dana više ne čita cjenik (prije: uvijek ponovno razrješavanje).
        AppointmentDto sameDay = await w.MoveOnlySegment(created.Id, SchedulingWorld.Future(12));
        Assert.DoesNotContain(sameDay.Warnings, x => x.Code == WarningCodes.ParticipationPriceChanged);
        Assert.Equal(50m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);

        AppointmentDto nextWeek = await w.MoveOnlySegment(created.Id, SchedulingWorld.Future(10).AddDays(7));
        Assert.Equal(60m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);
        WarningParticipationPriceChangedDetails changed = PriceChanged(nextWeek);
        Assert.Equal((participationId, w.Client.Id.Value, 50m, 60m, ParticipationPriceChangeReason.Time),
            (changed.ParticipationId, changed.ClientId, changed.OldAmount, changed.NewAmount, changed.Reason));
    }

    [Fact]
    public async Task ChangeSegmentEmployees_RereadsThePriceListOnlyWhenThePricingEmployeeChanges()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentEmployees_RereadsThePriceListOnlyWhenThePricingEmployeeChanges));
        Employee second = await w.AddEmployee("Second");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid segmentId = created.Segments.Single().Id;
        await w.AddPriceListItem(w.Service, 60m, FutureDate);

        // Drugi zaposlenik uz isti izvor cijene (prvi zaposlenik) — CHANGED in T1: cijena ostaje (prije: ponovno razrješavanje).
        AppointmentDto sameSource = await w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentEmployeesChangeRequest
            {
                EmployeeIds = new List<Guid> { w.Employee.Id.Value, second.Id.Value },
                PricingMode = SegmentPricingMode.Employee, PricingEmployeeId = w.Employee.Id
            });
        Assert.DoesNotContain(sameSource.Warnings, x => x.Code == WarningCodes.ParticipationPriceChanged);
        Assert.Equal(50m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);

        // Izvor cijene postaje drugi zaposlenik — cjenik se čita ponovno.
        AppointmentDto otherSource = await w.Appointments.ChangeSegmentEmployees(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentEmployeesChangeRequest { EmployeeIds = new List<Guid> { second.Id.Value } });
        Assert.Equal(60m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);
        Assert.Equal((50m, 60m, ParticipationPriceChangeReason.Employees),
            (PriceChanged(otherSource).OldAmount, PriceChanged(otherSource).NewAmount, PriceChanged(otherSource).Reason));
    }

    [Fact]
    public async Task ChangeSegmentService_RereadsThePriceListForANewService_AndChangeSegmentPricingSourceAlwaysDoes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeSegmentService_RereadsThePriceListForANewService_AndChangeSegmentPricingSourceAlwaysDoes));
        ServiceEntity other = await w.AddService(30, 80m, name: "Other");
        await w.AssignEmployeeToService(w.Employee, other);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid segmentId = created.Segments.Single().Id;
        await w.AddPriceListItem(w.Service, 60m, FutureDate);

        // Ista usluga (samo trajanje) — cijena ostaje.
        AppointmentDto sameService = await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentServiceChangeRequest { ServiceId = w.Service.Id.Value, PlannedEnd = SchedulingWorld.Future(10, 45) });
        Assert.DoesNotContain(sameService.Warnings, x => x.Code == WarningCodes.ParticipationPriceChanged);
        Assert.Equal(50m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);

        // Izvor cijene (isti zaposlenik) — cjenik se čita ponovno (kontekst cijene) i promjena se javlja.
        AppointmentDto pricing = await w.Appointments.ChangeSegmentPricingSource(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentPricingSourceChangeRequest { PricingMode = SegmentPricingMode.Employee, PricingEmployeeId = w.Employee.Id });
        Assert.Equal((50m, 60m, ParticipationPriceChangeReason.PricingSource),
            (PriceChanged(pricing).OldAmount, PriceChanged(pricing).NewAmount, PriceChanged(pricing).Reason));

        AppointmentDto newService = await w.Appointments.ChangeSegmentService(w.OrganizationId, w.ActorUserId, true, segmentId,
            new AppointmentSegmentServiceChangeRequest { ServiceId = other.Id.Value, PlannedEnd = SchedulingWorld.Future(10, 30) });
        Assert.Equal(80m, (await OnlyParticipation(w, created.Id, w.Client)).Amount);
        Assert.Equal((60m, 80m, ParticipationPriceChangeReason.Service),
            (PriceChanged(newService).OldAmount, PriceChanged(newService).NewAmount, PriceChanged(newService).Reason));
    }

    #endregion

    #region (5) Ručni iznos i provizija

    [Fact]
    public async Task ManualAmountBelowTheBase_IsTheCommissionBase_AndTheCommissionNeverExceedsTheChargedAmountInAnySwitchCombination()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualAmountBelowTheBase_IsTheCommissionBase_AndTheCommissionNeverExceedsTheChargedAmountInAnySwitchCombination));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        IOrganizationSettingsService settings = w.Resolve<IOrganizationSettingsService>();

        (bool Discounts, bool MembershipDiscounts)[] combinations = { (false, false), (true, false), (false, true), (true, true) };
        int hour = 9;
        foreach ((bool discounts, bool membershipDiscounts) in combinations)
        {
            await settings.UpdateCommissionSettings(w.OrganizationId, w.ActorUserId,
                new OrganizationCommissionSettingsDto
                {
                    DeductDiscounts = discounts, DeductMembershipDiscounts = membershipDiscounts, LateCancellation = CommissionLateCancellationMode.Never
                });
            await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(hour++), paymentMethod: PaymentMethod.Cash, settlementAmount: 40m));
        }

        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(4, entries.Count);
        // Lista 50 €, ručno 40 €: osnovica je naplaćeni iznos 40 € (uz "oduzmi popuste" i bez njega), provizija 4 € <= 40 €.
        Assert.All(entries, e => Assert.Equal((40m, 4m, 50m, 40m, true),
            (e.BaseAmount, e.CommissionAmount, e.ListPriceAmount.Value, e.SessionPriceAmount.Value, e.IsManualPrice.Value)));
        Assert.All(entries, e => Assert.True(e.CommissionAmount <= 40m));
        Assert.Contains(entries, e => e.DeductDiscounts == true);
    }

    [Fact]
    public async Task ManualAmountAboveTheBase_IsTheCommissionBase_WithTheDiscountSwitchOn()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualAmountAboveTheBase_IsTheCommissionBase_WithTheDiscountSwitchOn));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        await w.Resolve<IOrganizationSettingsService>().UpdateCommissionSettings(w.OrganizationId, w.ActorUserId,
            new OrganizationCommissionSettingsDto { DeductDiscounts = true, DeductMembershipDiscounts = true, LateCancellation = CommissionLateCancellationMode.Never });

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9), paymentMethod: PaymentMethod.Cash, settlementAmount: 70m));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((70m, 7m, 50m, true), (entry.BaseAmount, entry.CommissionAmount, entry.ListPriceAmount.Value, entry.IsManualPrice.Value));
    }

    #endregion

    #region (6) Preklapanje stavki navodi stavku s kojom se preklapa

    [Fact]
    public async Task PriceOverlap_AtTheBoundary_NamesTheConflictingItemWithItsDatesAndContext()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceOverlap_AtTheBoundary_NamesTheConflictingItemWithItsDatesAndContext));
        IPricingService pricing = w.Resolve<IPricingService>();
        DateOnly from = new(2031, 1, 1), to = new(2031, 1, 31);
        PriceListItemDto a = await pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, CompanyId = w.Company.Id, Price = 55m, ValidFrom = from, ValidTo = to
        });

        // B.ValidFrom == A.ValidTo: oba kraja su uključena, pa se dan 31.1. preklapa.
        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.PriceOverlap, () => pricing.Create(w.OrganizationId, w.ActorUserId,
            new PriceListItemCreateRequest
            {
                SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, CompanyId = w.Company.Id, Price = 60m, ValidFrom = to
            }));
        PriceOverlapDetails details = Assert.IsType<PriceOverlapDetails>(ex.Details);
        Assert.Equal((a.Id, from, (DateOnly?)to, PricingSubjectType.Service, w.Service.Id, (Guid?)null, w.Company.Id, (Guid?)null),
            (details.ConflictingItemId, details.ValidFrom, details.ValidTo, details.SubjectType, details.ServiceId, details.PackageId,
                details.CompanyId, details.EmployeeId));
        Assert.Contains("01.01.2031.", ex.Message);
        Assert.Contains("31.01.2031.", ex.Message);

        // Dan nakon ValidTo je valjan nastavak (bez preklapanja i bez rupe).
        PriceListItemDto b = await pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, CompanyId = w.Company.Id, Price = 60m, ValidFrom = to.AddDays(1)
        });
        Assert.DoesNotContain(b.Warnings, x => x.Code == WarningCodes.PriceListGap);
    }

    #endregion

    #region (7) Rupa u cjeniku: PRICE_NOT_DEFINED

    [Fact]
    public async Task PriceNotDefined_WhenTheServiceHasPriceListItemsButNoneCoversTheDate_RegardlessOfTheAmount()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceNotDefined_WhenTheServiceHasPriceListItemsButNoneCoversTheDate_RegardlessOfTheAmount));
        DateOnly past = SchedulingWorld.Day(SchedulingWorld.PastDay);
        await w.AddPriceListItem(w.Service, 55m, past, validTo: past.AddDays(10));

        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        WarningPriceNotDefinedDetails details = Details<WarningPriceNotDefinedDetails>(Assert.Single(created.Warnings, x => x.Code == WarningCodes.PriceNotDefined));
        Assert.Equal((w.Service.Id.Value, w.Service.Name, FutureDate, 50m, PriceSource.Default, PriceNotDefinedReason.NoPriceListItemForDate),
            (details.ServiceId, details.ServiceName, details.Date, details.UsedAmount, details.Source, details.Reason));
        Assert.Equal(50m, (await OnlyParticipation(w, created.Id, w.Client)).Amount); // rezervacija se ne odbija
    }

    [Fact]
    public async Task PriceNotDefined_ForAZeroDefaultPrice_AndNoWarningForANonZeroDefaultWithoutPriceListItems()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceNotDefined_ForAZeroDefaultPrice_AndNoWarningForANonZeroDefaultWithoutPriceListItems));
        ServiceEntity free = await w.AddService(30, 0m, name: "Free");
        await w.AssignEmployeeToService(w.Employee, free);

        AppointmentDto normal = await w.CreateAppointment(SchedulingWorld.Future(9));
        Assert.DoesNotContain(normal.Warnings, x => x.Code == WarningCodes.PriceNotDefined);

        AppointmentDto zero = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(11), service: free));
        WarningPriceNotDefinedDetails details = Details<WarningPriceNotDefinedDetails>(Assert.Single(zero.Warnings, x => x.Code == WarningCodes.PriceNotDefined));
        Assert.Equal((free.Id.Value, 0m, PriceNotDefinedReason.ZeroDefaultPrice), (details.ServiceId, details.UsedAmount, details.Reason));
    }

    [Fact]
    public async Task PriceNotDefined_GroupGeneration_ReturnsOneAggregatedWarningWithTheOccurrenceCountAndDates_AndAGuestGetsItToo()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceNotDefined_GroupGeneration_ReturnsOneAggregatedWarningWithTheOccurrenceCountAndDates_AndAGuestGetsItToo));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);
        DateOnly past = SchedulingWorld.Day(SchedulingWorld.PastDay);
        await w.AddPriceListItem(groupService, 20m, past, validTo: past.AddDays(10));
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        await w.AddGroupMember(group, w.Client);

        GenerateGroupAppointmentsResult result = await w.GenerateOccurrences(group, SchedulingWorld.FutureDay, SchedulingWorld.FutureDay.AddDays(7));

        Assert.Equal(2, result.CreatedCount);
        WarningDto warning = Assert.Single(result.Warnings);
        Assert.Equal(WarningCodes.PriceNotDefinedOccurrences, warning.Code);
        WarningPriceNotDefinedOccurrencesDetails details = Details<WarningPriceNotDefinedOccurrencesDetails>(warning);
        Assert.Equal(2, details.OccurrenceCount);
        Assert.Equal(new[] { FutureDate, FutureDate.AddDays(7) }, details.Dates);
        Assert.All(details.Items, i => Assert.Equal((groupService.Id.Value, 15m, PriceNotDefinedReason.NoPriceListItemForDate), (i.ServiceId, i.UsedAmount, i.Reason)));

        Appointment first = await w.LoadAppointment(result.Created.OrderBy(c => c.PlannedStart).First().Id);
        BookingDto guest = await w.AddGuest(first, await w.AddClient("Guest", "Gap"));
        Assert.Equal(PriceNotDefinedReason.NoPriceListItemForDate,
            Details<WarningPriceNotDefinedDetails>(Assert.Single(guest.Warnings, x => x.Code == WarningCodes.PriceNotDefined)).Reason);
    }

    #endregion

    #region (8) Upozorenja spremanja stavke cjenika

    [Fact]
    public async Task PriceListSave_ReportsAGapBetweenTwoItemsOfTheSameContext_AndIsNeverRefused()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceListSave_ReportsAGapBetweenTwoItemsOfTheSameContext_AndIsNeverRefused));
        IPricingService pricing = w.Resolve<IPricingService>();
        DateOnly from = new(2031, 1, 1), to = new(2031, 1, 31);
        await pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, Price = 55m, ValidFrom = from, ValidTo = to
        });

        PriceListItemDto later = await pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, Price = 60m, ValidFrom = new DateOnly(2031, 2, 10)
        });

        WarningPriceListGapDetails gap = Details<WarningPriceListGapDetails>(Assert.Single(later.Warnings, x => x.Code == WarningCodes.PriceListGap));
        Assert.Equal((new DateOnly(2031, 2, 1), new DateOnly(2031, 2, 9)), (gap.FromDate, gap.ToDate));

        // Zatvaranje rupe izmjenom: bez upozorenja.
        PriceListItemDto closed = await pricing.Update(w.OrganizationId, w.ActorUserId, later.Id,
            new PriceListItemUpdateRequest { Price = 60m, ValidFrom = new DateOnly(2031, 2, 1) });
        Assert.DoesNotContain(closed.Warnings, x => x.Code == WarningCodes.PriceListGap);
    }

    [Fact]
    public async Task PriceListSave_StatesHowManyScheduledParticipationsKeepTheirStoredPrice_AndDoesNotReprice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PriceListSave_StatesHowManyScheduledParticipationsKeepTheirStoredPrice_AndDoesNotReprice));
        IPricingService pricing = w.Resolve<IPricingService>();
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto inside = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second); // 2 sudjelovanja
        AppointmentDto outside = await w.CreateAppointment(SchedulingWorld.Future(10).AddDays(7));

        PriceListItemDto item = await pricing.Create(w.OrganizationId, w.ActorUserId, new PriceListItemCreateRequest
        {
            SubjectType = PricingSubjectType.Service, ServiceId = w.Service.Id, CompanyId = w.Company.Id, Price = 65m,
            ValidFrom = FutureDate, ValidTo = FutureDate.AddDays(1)
        });

        WarningPriceListScheduledKeepDetails keep = Details<WarningPriceListScheduledKeepDetails>(
            Assert.Single(item.Warnings, x => x.Code == WarningCodes.PriceListScheduledKeepOldPrice));
        Assert.Equal((2, FutureDate, (DateOnly?)FutureDate.AddDays(1)), (keep.Count, keep.ValidFrom, keep.ValidTo));
        Assert.All(await ParticipationsOf(w, inside.Id), p => Assert.Equal(50m, p.Amount)); // primjena na zakazane = P5
        Assert.Equal(50m, (await OnlyParticipation(w, outside.Id, w.Client)).Amount);
    }

    #endregion

    #region (9) Izvještaj provizija: zarađeno u P1, storno i ponovno zarađeno u P2

    [Fact]
    public async Task CommissionReport_EarnedInP1_ReversedAndReEarnedInP2_LeavesP1Unchanged_AndShowsBothInP2()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CommissionReport_EarnedInP1_ReversedAndReEarnedInP2_LeavesP1Unchanged_AndShowsBothInP2));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);

        // Zarada pripada razdoblju P1 (40 dana ranije).
        DateTimeOffset earnedInP1 = TestClock.UtcNow.AddDays(-40);
        await using (DatabaseContext db = w.NewDb())
        {
            CommissionEntry original = await db.CommissionEntries.SingleAsync(e => e.OrganizationId == w.OrganizationId);
            original.EarnedAt = earnedInP1;
            await db.SaveChangesAsync();
        }

        // Korekcija u P2: Completed -> Confirmed (storno) -> Completed (ponovno zarađeno).
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.CreatedAt).ToList();
        Assert.Equal(new[] { CommissionEntryStatus.Reversed, CommissionEntryStatus.Earned }, entries.Select(e => e.Status));

        ICommissionService ledger = w.Resolve<ICommissionService>();
        DateOnly p1 = DateOnly.FromDateTime(earnedInP1.UtcDateTime);
        DateOnly today = DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime);

        EmployeeCommissionSummaryDto inP1 = Assert.Single((await ledger.GetSummary(w.OrganizationId,
            new CommissionSummaryQuery { From = p1.AddDays(-1), To = p1.AddDays(1) })).Employees);
        Assert.Equal((5m, 0m, 5m), (inP1.EarnedAmount, inP1.ReversedAmount, inP1.NetAmount));

        EmployeeCommissionSummaryDto inP2 = Assert.Single((await ledger.GetSummary(w.OrganizationId,
            new CommissionSummaryQuery { From = today.AddDays(-1), To = today.AddDays(1) })).Employees);
        Assert.Equal((5m, 5m, 0m), (inP2.EarnedAmount, inP2.ReversedAmount, inP2.NetAmount));
    }

    #endregion
}
