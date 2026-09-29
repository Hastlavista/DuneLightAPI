#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntityAlias = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// CHARACTERIZATION (matrix O): package coverage — how a ClientPackage entitlement is checked, consumed, returned and
/// kept mutually exclusive with money.
///
/// Rules observed:
///  * Eligibility (ClientPackageHandler.GetEligibleForService) is evaluated against the APPOINTMENT DATE: status Active,
///    ExpiryDate &gt;= date, the package covers the service, and a positive (or unlimited = null) remaining counter.
///  * Consumption (ClientPackageEntryMutator.Deduct) happens inside the completion transaction and is judged against the
///    REAL clock (a second, different notion of "expired").
///  * Coverage never creates a Payment. Package coverage and money are mutually exclusive for one Booking (XOR),
///    enforced in code in several places — there is no database constraint.
///
/// The first region tests the pure mutator; the rest go through the real completion / check-in flows.
/// </summary>
public class PackageCoverageCharacterizationTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2031, 3, 3, 12, 0, 0, TimeSpan.Zero);

    #region The entry mutator in isolation

    private static ClientPackage PerService(Guid serviceId, int? remaining, int? total = null, ClientPackageStatus status = ClientPackageStatus.Active, DateTimeOffset? expiry = null) =>
        new()
        {
            EntryMode = PackageEntryMode.PerService,
            Status = status,
            ExpiryDate = expiry ?? Now.AddYears(1),
            ServiceEntries = { new ClientPackageServiceEntry { ServiceId = serviceId, RemainingEntries = remaining, TotalEntries = total ?? remaining } }
        };

    [Fact]
    public void Deduct_DecrementsTheServiceEntryByOne()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 5);

        ClientPackageEntryMutator.Deduct(package, service, Now);

        Assert.Equal(4, package.ServiceEntries.Single().RemainingEntries);
        Assert.Equal(ClientPackageStatus.Active, package.Status);
    }

    [Fact]
    public void Deduct_TheLastEntry_MarksThePackageDepleted()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 1);

        ClientPackageEntryMutator.Deduct(package, service, Now);

        Assert.Equal(0, package.ServiceEntries.Single().RemainingEntries);
        Assert.Equal(ClientPackageStatus.Depleted, package.Status);
    }

    [Fact]
    public void Deduct_WhenNoEntriesRemain_IsRejected()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 0, total: 5);

        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => ClientPackageEntryMutator.Deduct(package, service, Now));

        Assert.Equal(ErrorCodes.PackageNotEligible, ex.Code);
    }

    [Fact]
    public void Deduct_AnUnlimitedEntry_ChangesNothing_ButIsNotAnError()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, remaining: null);

        ClientPackageEntryMutator.Deduct(package, service, Now);

        Assert.Null(package.ServiceEntries.Single().RemainingEntries);
        Assert.Equal(ClientPackageStatus.Active, package.Status);
    }

    [Fact]
    public void Deduct_ForAServiceThePackageDoesNotCover_IsRejected()
    {
        ClientPackage package = PerService(Guid.NewGuid(), 5);

        BusinessRuleException ex = Assert.Throws<BusinessRuleException>(() => ClientPackageEntryMutator.Deduct(package, Guid.NewGuid(), Now));

        Assert.Equal(ErrorCodes.PackageServiceNotCovered, ex.Code);
    }

    [Theory]
    [InlineData(ClientPackageStatus.Cancelled)]
    public void Deduct_ACancelledPackage_IsRejected(ClientPackageStatus status)
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 5, status: status);

        Assert.Equal(ErrorCodes.PackageNotEligible,
            Assert.Throws<BusinessRuleException>(() => ClientPackageEntryMutator.Deduct(package, service, Now)).Code);
    }

    [Fact]
    public void Deduct_AnExpiredPackage_IsRejected_JudgedAgainstTheClockPassedIn()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 5, expiry: Now.AddDays(-1));

        Assert.Equal(ErrorCodes.PackageNotEligible,
            Assert.Throws<BusinessRuleException>(() => ClientPackageEntryMutator.Deduct(package, service, Now)).Code);
    }

    [Fact]
    public void Return_GivesAnEntryBack_ButNeverAboveTheOriginalTotal()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, remaining: 4, total: 5);

        ClientPackageEntryMutator.Return(package, service, Now);
        ClientPackageEntryMutator.Return(package, service, Now); // a repeated / concurrent return must not inflate the package

        Assert.Equal(5, package.ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public void Return_ReactivatesADepletedPackage_UnlessItHasExpiredInTheMeantime()
    {
        Guid service = Guid.NewGuid();
        ClientPackage active = PerService(service, 0, total: 5, status: ClientPackageStatus.Depleted, expiry: Now.AddDays(10));
        ClientPackage expired = PerService(service, 0, total: 5, status: ClientPackageStatus.Depleted, expiry: Now.AddDays(-10));

        ClientPackageEntryMutator.Return(active, service, Now);
        ClientPackageEntryMutator.Return(expired, service, Now);

        Assert.Equal(ClientPackageStatus.Active, active.Status);
        Assert.Equal(ClientPackageStatus.Depleted, expired.Status);
    }

    [Fact]
    public void Return_NeverResurrectsACancelledPackage()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = PerService(service, 4, total: 5, status: ClientPackageStatus.Cancelled);

        ClientPackageEntryMutator.Return(package, service, Now);

        Assert.Equal(ClientPackageStatus.Cancelled, package.Status);
    }

    [Fact]
    public void SharedPool_DeductsFromThePooledCounter_AndDepletesAtZero()
    {
        Guid service = Guid.NewGuid();
        ClientPackage package = new()
        {
            EntryMode = PackageEntryMode.SharedPool,
            Status = ClientPackageStatus.Active,
            ExpiryDate = Now.AddYears(1),
            TotalEntryCount = 2,
            RemainingSharedEntries = 1,
            ServiceEntries = { new ClientPackageServiceEntry { ServiceId = service } }
        };

        ClientPackageEntryMutator.Deduct(package, service, Now);

        Assert.Equal(0, package.RemainingSharedEntries);
        Assert.Equal(ClientPackageStatus.Depleted, package.Status);
    }

    #endregion

    #region Individual completion — eligibility

    [Fact]
    public async Task Individual_AnEligiblePackage_IsConsumed_AndTheLastEntryDepletesIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_AnEligiblePackage_IsConsumed_AndTheLastEntryDepletesIt));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 1, LongValid);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        ClientPackage after = await w.LoadClientPackage(package.Id.Value);
        Assert.Equal(0, after.ServiceEntries.Single().RemainingEntries);
        Assert.Equal(ClientPackageStatus.Depleted, after.Status);
    }

    [Fact]
    public async Task Individual_ADepletedPackage_CannotCoverAnotherBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ADepletedPackage_CannotCoverAnotherBooking));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 0, LongValid);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));

        Assert.Equal(0, await w.CountAppointments());
    }

    [Fact]
    public async Task Individual_APackageCoveringADifferentService_IsNotEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_APackageCoveringADifferentService_IsNotEligible));
        ServiceEntityAlias other = await w.AddService(30, 10m);
        ClientPackage package = await w.AddClientPackage(w.Client, other, 5, LongValid);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));
    }

    [Fact]
    public async Task Individual_APackageBelongingToAnotherClient_IsNotEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_APackageBelongingToAnotherClient_IsNotEligible));
        Client owner = await w.AddClient("Owner", "Client");
        ClientPackage package = await w.AddClientPackage(owner, w.Service, 5, LongValid);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));
    }

    [Theory]
    [InlineData(ClientPackageStatus.Cancelled)]
    [InlineData(ClientPackageStatus.Expired)]
    [InlineData(ClientPackageStatus.Depleted)]
    public async Task Individual_APackageThatIsNotActive_IsNotEligible(ClientPackageStatus status)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(Individual_APackageThatIsNotActive_IsNotEligible)}-{status}");
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid, status: status);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));
    }

    [Fact]
    public async Task Individual_APackageThatExpiresBeforeTheAppointmentDate_IsNotEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_APackageThatExpiresBeforeTheAppointmentDate_IsNotEligible));
        // Valid until 2035, but the appointment is in 2040: eligibility is judged at the APPOINTMENT date.
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(new DateTimeOffset(2040, 3, 5, 10, 0, 0, TimeSpan.Zero));

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteExisting(created.Id, w.CompleteRequest(new DateTimeOffset(2040, 3, 5, 10, 0, 0, TimeSpan.Zero), clientPackageId: package.Id)));
    }

    [Fact]
    public async Task Individual_APackageValidOnAPastAppointmentDateButExpiredToday_PassesEligibilityThenFailsAtDeduction()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_APackageValidOnAPastAppointmentDateButExpiredToday_PassesEligibilityThenFailsAtDeduction));
        // Expires 2021-01-01: valid on the 2020 appointment date, long expired on the real clock.
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));

        // FINDING: two clocks. Eligibility uses the appointment date (passes), deduction uses "now" (fails), so back-dating a
        // completion onto a package that has since expired is rejected at the last step — and the whole transaction rolls back.
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));

        Assert.Equal(0, await w.CountAppointments());
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task Individual_AnUnlimitedPackage_AppliesCoverageWithoutChangingAnyCounter()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_AnUnlimitedPackage_AppliesCoverageWithoutChangingAnyCounter));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, entries: null, LongValid);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        Booking b = (await w.LoadAppointment(dto.Id)).Bookings.Single();
        Assert.True(b.PackageCoverageApplied); // "applied" does not mean "a counter was decremented"
        Assert.Null((await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.True(Assert.Single(dto.Bookings).IsPaid);
    }

    [Fact]
    public async Task Individual_ASharedPoolPackage_DeductsFromThePool()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_ASharedPoolPackage_DeductsFromThePool));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 3, LongValid, PackageEntryMode.SharedPool);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        Assert.Equal(2, (await w.LoadClientPackage(package.Id.Value)).RemainingSharedEntries);
    }

    [Fact]
    public async Task Individual_SelectingAPackageOnAConfirmedBooking_HasNoEffectUntilCompletion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Individual_SelectingAPackageOnAConfirmedBooking_HasNoEffectUntilCompletion));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);

        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // Create has no settlement input at all: a Confirmed booking never carries package coverage.
        Booking b = (await w.LoadAppointment(created.Id)).Bookings.Single();
        Assert.Null(b.ClientPackageId);
        Assert.False(b.PackageCoverageApplied);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    #endregion

    #region The XOR rule: package coverage vs. monetary payment

    [Fact]
    public async Task Xor_ApplyingAPackageToABookingThatAlreadyHasAManualPayment_IsRefused_AndRollsBack()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_ApplyingAPackageToABookingThatAlreadyHasAManualPayment_IsRefused_AndRollsBack));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 20m);

        BusinessRuleException ex = await SchedulingAssert.BusinessRule(ErrorCodes.BookingAlreadyHasMonetaryPayment,
            () => w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id)));

        Assert.NotNull(ex.Details);
        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(BookingStatus.Confirmed, b.Status);
        Assert.Null(b.ClientPackageId);
        Assert.Equal(5, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status);
    }

    [Fact]
    public async Task Xor_AVoidedManualPayment_NoLongerBlocksApplyingAPackage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_AVoidedManualPayment_NoLongerBlocksApplyingAPackage));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        Guid checkoutId = await w.PayBookingViaCheckout(bookingId, w.Client, 20m);
        Payment payment = Assert.Single(await w.LoadPayments(bookingId));
        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkoutId, payment.Id.Value, new CheckoutPaymentVoidRequest { Reason = "mistake" });

        AppointmentDto completed = await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));

        Assert.True(Assert.Single(completed.Bookings).PackageCoverageApplied);
    }

    [Fact]
    public async Task Xor_ACheckInPaymentCannotBeRecordedForABookingThatCarriesAPackage_EvenAfterTheCoverageWasReturned()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_ACheckInPaymentCannotBeRecordedForABookingThatCarriesAPackage_EvenAfterTheCoverageWasReturned));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), clientPackageId: package.Id));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction: coverage returned, link kept

        // FINDING: the returned coverage leaves Booking.ClientPackageId set, and the check-in payment guard tests only that
        // column. Re-completing the same booking as a CASH sale is therefore refused although nothing settles it any more.
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentNotAllowed,
            () => w.CompleteExisting(created.Id, w.CompleteRequest(SchedulingWorld.Future(10), paymentMethod: PaymentMethod.Cash)));

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);
    }

    [Fact]
    public async Task Xor_ACheckoutPaymentAgainstAPackageSettledBooking_IsRefused_WhenNothingElseIsOutstanding()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_ACheckoutPaymentAgainstAPackageSettledBooking_IsRefused_WhenNothingElseIsOutstanding));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto completed = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        Guid bookingId = completed.Bookings.Single().Id;
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        CheckoutDto withItem = await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { BookingId = bookingId });
        CheckoutItemDto item = Assert.Single(withItem.Items);
        // The item shows the retail price but no monetary due: the package settles it.
        Assert.Equal(50m, item.RetailAmount);
        Assert.Equal(0m, item.MonetaryDue);
        Assert.Equal(0m, item.OutstandingAmount);

        // FIFO mode: nothing is outstanding, so any amount exceeds it.
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount,
            () => w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest { Amount = 10m, Method = PaymentMethod.Cash }));
        // Explicit mode fails the same way: the checkout-level outstanding check runs before per-item allocation rules.
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount,
            () => w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest
            {
                Amount = 10m, Method = PaymentMethod.Cash,
                Allocations = new List<CheckoutPaymentAllocationRequest> { new() { CheckoutItemId = item.Id, Amount = 10m } }
            }));
    }

    [Fact]
    public async Task Xor_AnExplicitAllocationToAPackageSettledItem_IsRefusedAsAMixedSettlement_WhenAnotherItemIsOutstanding()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_AnExplicitAllocationToAPackageSettledItem_IsRefusedAsAMixedSettlement_WhenAnotherItemIsOutstanding));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto covered = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        AppointmentDto unpaid = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(12))); // Completed, no settlement: 50 outstanding
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { BookingId = covered.Bookings.Single().Id });
        CheckoutDto both = await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { BookingId = unpaid.Bookings.Single().Id });
        CheckoutItemDto coveredItem = both.Items.Single(i => i.BookingId == covered.Bookings.Single().Id);

        // Now the checkout has 50 outstanding, so the amount check passes and the per-item package rule is what refuses it.
        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentNotAllowed,
            () => w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest
            {
                Amount = 10m, Method = PaymentMethod.Cash,
                Allocations = new List<CheckoutPaymentAllocationRequest> { new() { CheckoutItemId = coveredItem.Id, Amount = 10m } }
            }));
    }

    [Fact]
    public async Task Xor_AFifoPaymentInAMixedCheckout_SkipsThePackageSettledItemAndPaysTheOther()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Xor_AFifoPaymentInAMixedCheckout_SkipsThePackageSettledItemAndPaysTheOther));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto covered = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        AppointmentDto unpaid = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(12)));
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { BookingId = covered.Bookings.Single().Id });
        await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { BookingId = unpaid.Bookings.Single().Id });

        CheckoutDto paid = await w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutPaymentCreateRequest { Amount = 50m, Method = PaymentMethod.Cash });

        Assert.Equal(0m, paid.Items.Single(i => i.BookingId == covered.Bookings.Single().Id).PaidAmount);
        Assert.Equal(50m, paid.Items.Single(i => i.BookingId == unpaid.Bookings.Single().Id).PaidAmount);
        Assert.True(paid.Totals.IsFullyPaid);
        Assert.Equal(100m, paid.Totals.RetailTotal);   // retail counts both items ...
        Assert.Equal(50m, paid.Totals.MonetaryDue);    // ... but only the uncovered one is owed in money
    }

    #endregion

    #region Group check-in coverage resolution

    private static async Task<(Appointment Occurrence, Client Member)> GroupOccurrence(SchedulingWorld w, ServiceEntityAlias svc)
    {
        var group = await w.CreateGroup(svc, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        return (await w.GenerateSingleOccurrence(group), w.Client);
    }

    [Fact]
    public async Task Group_CheckIn_WithASingleEligibleSessionPackage_AutoSelectsIt_AndDeductsOneEntry()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WithASingleEligibleSessionPackage_AutoSelectsIt_AndDeductsOneEntry));
        ServiceEntityAlias svc = await w.AddGroupService();
        ClientPackage package = await w.AddClientPackage(w.Client, svc, 5, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(package.Id, b.ClientPackageId);
        Assert.Equal(AttendanceCoverageType.SessionPackage, b.CoverageType);
        Assert.True(b.PackageCoverageApplied);
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Contains(await w.LoadAuditLog(occurrence.Id.Value), l => l.ChangeType == "BookingPackageCoverageApplied");
        Assert.Empty(await w.LoadPayments(b.Id.Value));
    }

    [Fact]
    public async Task Group_CheckIn_WithAnUnlimitedPackage_IsAMonthlyPackageCoverage_WithoutADeduction()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WithAnUnlimitedPackage_IsAMonthlyPackageCoverage_WithoutADeduction));
        ServiceEntityAlias svc = await w.AddGroupService();
        ClientPackage package = await w.AddClientPackage(w.Client, svc, entries: null, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(AttendanceCoverageType.MonthlyPackage, b.CoverageType);
        Assert.True(b.PackageCoverageApplied);
        Assert.Null((await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task Group_CheckIn_WithNoEligiblePackage_IsASinglePaidVisit_AndNoPaymentUnlessRequested()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WithNoEligiblePackage_IsASinglePaidVisit_AndNoPaymentUnlessRequested));
        ServiceEntityAlias svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Assert.Equal(AttendanceCoverageType.SinglePaid, b.CoverageType);
        Assert.False(b.PackageCoverageApplied);
        Assert.Equal(15m, b.Amount);
        Assert.Empty(await w.LoadPayments(b.Id.Value));
        Assert.False((await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value)).Bookings.Single().IsPaid);
    }

    [Fact]
    public async Task Group_CheckIn_WithSeveralEligiblePackages_RequiresAnExplicitChoice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WithSeveralEligiblePackages_RequiresAnExplicitChoice));
        ServiceEntityAlias svc = await w.AddGroupService();
        ClientPackage first = await w.AddClientPackage(w.Client, svc, 5, LongValid);
        ClientPackage second = await w.AddClientPackage(w.Client, svc, 5, LongValid.AddYears(1));
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await SchedulingAssert.Validation(() => w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed));
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, clientPackageId: second.Id);

        Assert.Equal(second.Id, (await w.LoadBooking(occurrence.Id.Value, member)).ClientPackageId);
        Assert.Equal(5, (await w.LoadClientPackage(first.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(4, (await w.LoadClientPackage(second.Id.Value)).ServiceEntries.Single().RemainingEntries);
    }

    [Fact]
    public async Task Group_CheckIn_WithAnExplicitIneligiblePackage_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WithAnExplicitIneligiblePackage_IsRejected));
        ServiceEntityAlias svc = await w.AddGroupService();
        ClientPackage depleted = await w.AddClientPackage(w.Client, svc, 0, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, clientPackageId: depleted.Id));

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(occurrence.Id.Value, member)).Status);
    }

    [Fact]
    public async Task Group_CheckIn_WhenAManualPaymentAlreadyExists_RefusesToApplyAPackage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_WhenAManualPaymentAlreadyExists_RefusesToApplyAPackage));
        ServiceEntityAlias svc = await w.AddGroupService();
        await w.AddClientPackage(w.Client, svc, 5, LongValid);
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);
        Guid bookingId = (await w.LoadBooking(occurrence.Id.Value, member)).Id.Value;
        await w.PayBookingViaCheckout(bookingId, member, 5m);

        await SchedulingAssert.BusinessRule(ErrorCodes.BookingAlreadyHasMonetaryPayment,
            () => w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed));

        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(occurrence.Id.Value, member)).Status);
    }

    [Fact]
    public async Task Group_CheckIn_AsASinglePaidVisit_WithAPaymentMethod_CreatesACheckInGeneratedPayment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Group_CheckIn_AsASinglePaidVisit_WithAPaymentMethod_CreatesACheckInGeneratedPayment));
        ServiceEntityAlias svc = await w.AddGroupService();
        (Appointment occurrence, Client member) = await GroupOccurrence(w, svc);

        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Completed, paymentMethod: PaymentMethod.Card);

        Booking b = await w.LoadBooking(occurrence.Id.Value, member);
        Payment payment = Assert.Single(await w.LoadPayments(b.Id.Value));
        Assert.Equal(15m, payment.Amount);
        Assert.True(payment.IsCheckInGenerated);
        Assert.Equal(PaymentMethod.Card, payment.Method);
    }

    #endregion
}
