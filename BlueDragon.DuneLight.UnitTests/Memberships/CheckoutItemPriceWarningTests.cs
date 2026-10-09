#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.UnitTests.Scheduling;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// Review of 2E — an open checkout warns when a session item's amount (snapshot when added) differs from the session's current
/// due, whether the price changed manually (P1) or automatically because of a membership. The item is never changed
/// automatically; reception refreshes it by removing and re-adding it before closing.
/// </summary>
public class CheckoutItemPriceWarningTests
{
    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(TestClock.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);

    private static async Task<(CheckoutDto Checkout, Guid ParticipationId)> CheckoutWith(SchedulingWorld w, AppointmentDto booked)
    {
        Guid participationId = booked.Bookings.Single().Participations.Single().Id;
        CheckoutDto checkout = await w.Checkouts.Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        checkout = await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { ParticipationId = participationId });
        Assert.Empty(checkout.Warnings);
        return (checkout, participationId);
    }

    private static WarningCheckoutItemPrice Changed(CheckoutDto checkout) =>
        Assert.Single(Assert.IsType<WarningCheckoutItemPriceDetails>(
            Assert.Single(checkout.Warnings, x => x.Code == WarningCodes.CheckoutItemPriceChanged).Details).Items);

    [Fact]
    public async Task ManualPriceChangeAfterAddingTheItem_IsWarnedOnPayment_AndTheItemIsNotChanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ManualPriceChangeAfterAddingTheItem_IsWarnedOnPayment_AndTheItemIsNotChanged));
        AppointmentDto booked = await w.CreateAppointment(w.CreateRequest(At(2, 10), overrideAvailability: true));
        (CheckoutDto checkout, Guid participationId) = await CheckoutWith(w, booked);

        await w.SetParticipationPrice(booked.Id, w.Client, 45m);
        CheckoutDto paid = await w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutPaymentCreateRequest { Amount = 10m, Method = PaymentMethod.Cash });

        WarningCheckoutItemPrice changed = Changed(paid);
        Assert.Equal((participationId, 50m, 45m), (changed.ParticipationId, changed.ItemAmount, changed.CurrentDue));
        Assert.Equal(50m, Assert.Single(paid.Items).UnitPrice); // never changed automatically
    }

    [Fact]
    public async Task SessionBecomingCoveredWhileInAnOpenCheckout_IsWarned_AndRemovingTheItemClearsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SessionBecomingCoveredWhileInAnOpenCheckout_IsWarned_AndRemovingTheItemClearsIt));
        MembershipPlanDto plan = await w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = "Gold", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            UsageLimits = new List<MembershipUsageLimitDto> { new() { Window = MembershipUsageWindow.Period, MaxUses = 1 } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        await w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });
        AppointmentDto first = await w.CreateAppointment(w.CreateRequest(At(2, 10), overrideAvailability: true));
        AppointmentDto overLimit = await w.CreateAppointment(w.CreateRequest(At(3, 10), overrideAvailability: true));
        (CheckoutDto checkout, Guid participationId) = await CheckoutWith(w, overLimit);

        await w.SetBookingStatus(first.Id, w.Client, BookingStatus.Cancelled, "on time"); // freed slot covers the item's session

        CheckoutDto read = await w.Checkouts.GetById(w.OrganizationId, checkout.Id);
        Assert.Equal((participationId, 50m, 0m), (Changed(read).ParticipationId, Changed(read).ItemAmount, Changed(read).CurrentDue));

        CheckoutDto refreshed = await w.Checkouts.RemoveItem(w.OrganizationId, w.ActorUserId, checkout.Id, Assert.Single(read.Items).Id);
        Assert.Empty(refreshed.Warnings);
    }
}
