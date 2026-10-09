#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2C — periods, charges and renewal against PostgreSQL (worlds use the UTC calendar; the renewal run takes a
/// simulated "today"): first period + start fee at sale, idempotent renewal, scheduled terms applied at the renewal, a client
/// change to a since-deactivated plan not applied (persistent mark), plan deactivation ending at the renewal (reactivation
/// continues), automatic end after N unpaid periods (debt stays), paying a charge through checkout (partial, projection),
/// write-off, void-sale guards and the delinquency rule for pauses.
/// </summary>
public class MembershipBillingTests
{
    private static IClientMembershipService Memberships(SchedulingWorld w) => w.Resolve<IClientMembershipService>();
    private static IMembershipPlanService Plans(SchedulingWorld w) => w.Resolve<IMembershipPlanService>();
    private static IMembershipRenewalService Renewal(SchedulingWorld w) => w.Resolve<IMembershipRenewalService>();
    private static ICheckoutService Checkouts(SchedulingWorld w) => w.Resolve<ICheckoutService>();
    private static DateOnly Today => DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime);

    private static async Task<MembershipPlanDto> Plan(SchedulingWorld w, string name, decimal price, decimal startFee = 0m,
        MembershipRenewalAnchor anchor = MembershipRenewalAnchor.PurchaseDate) =>
        await Plans(w).Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = name, Price = price, StartFee = startFee, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = anchor,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            Pause = anchor == MembershipRenewalAnchor.PurchaseDate
                ? new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, ExtendsPeriod = true }
                : new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 2 }
        });

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, Guid planId) =>
        Memberships(w).Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value, new ClientMembershipSellRequest
        {
            MembershipPlanId = planId, SoldCompanyId = w.Company.Id.Value
        });

    private static Task<List<MembershipChargeDto>> Charges(SchedulingWorld w, Guid membershipId) =>
        Memberships(w).GetCharges(w.OrganizationId, membershipId);

    [Fact]
    public async Task Sale_OpensTheFirstPeriod_WithAPeriodChargeAndAStartFee_DueOnTheStartDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Sale_OpensTheFirstPeriod_WithAPeriodChargeAndAStartFee_DueOnTheStartDate));
        MembershipPlanDto plan = await Plan(w, "Gold", 50m, startFee: 20m);

        ClientMembershipDto sold = await Sell(w, plan.Id);

        MembershipPeriodDto period = Assert.Single(await Memberships(w).GetPeriods(w.OrganizationId, sold.Id));
        Assert.Equal((Today, Today.AddMonths(1).AddDays(-1)), (period.StartsOn, period.EndsOn));
        List<MembershipChargeDto> charges = await Charges(w, sold.Id);
        Assert.Equal(new[] { (MembershipChargeKind.Period, 50m), (MembershipChargeKind.StartFee, 20m) },
            charges.OrderBy(c => c.Kind).Select(c => (c.Kind, c.Amount)));
        Assert.All(charges, c => Assert.Equal((Today, MembershipChargeStatus.Pending), (c.DueOn, c.Status)));
        Assert.Equal((MembershipStanding.InGrace, 70m), (sold.Standing, sold.OutstandingAmount));
    }

    [Fact]
    public async Task Renewal_OpensTheNextPeriodWithItsCharge_AndIsIdempotent()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Renewal_OpensTheNextPeriodWithItsCharge_AndIsIdempotent));
        MembershipPlanDto plan = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, plan.Id);

        DateOnly renewalDay = Today.AddMonths(1);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, renewalDay.AddDays(-1));
        Assert.Single(await Memberships(w).GetPeriods(w.OrganizationId, sold.Id));

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, renewalDay);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, renewalDay);
        List<MembershipPeriodDto> periods = await Memberships(w).GetPeriods(w.OrganizationId, sold.Id);
        Assert.Equal(new[] { renewalDay, Today }, periods.Select(p => p.StartsOn));
        Assert.Equal(2, (await Charges(w, sold.Id)).Count(c => c.Kind == MembershipChargeKind.Period));

        // Catch-up: a missed run opens every period that has started since.
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(3));
        Assert.Equal(4, (await Memberships(w).GetPeriods(w.OrganizationId, sold.Id)).Count);
    }

    [Fact]
    public async Task ScheduledPlanChange_IsAppliedAtTheRenewal_WithTheNewPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ScheduledPlanChange_IsAppliedAtTheRenewal_WithTheNewPrice));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        MembershipPlanDto silver = await Plan(w, "Silver", 30m);
        ClientMembershipDto sold = await Sell(w, gold.Id);
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = silver.Id });

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));

        ClientMembershipDto renewed = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal((silver.Id, silver.LatestVersion.Id), (renewed.MembershipPlanId, renewed.Terms.Id));
        Assert.Null(renewed.PendingChange);
        MembershipChargeDto latest = (await Charges(w, sold.Id)).Where(c => c.Kind == MembershipChargeKind.Period).OrderByDescending(c => c.DueOn).First();
        Assert.Equal((30m, Today.AddMonths(1)), (latest.Amount, latest.DueOn));
    }

    [Fact]
    public async Task ChangeToAPlanDeactivatedBeforeTheRenewal_IsNotApplied_AndLeavesAMark()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeToAPlanDeactivatedBeforeTheRenewal_IsNotApplied_AndLeavesAMark));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        MembershipPlanDto silver = await Plan(w, "Silver", 30m);
        ClientMembershipDto sold = await Sell(w, gold.Id);
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = silver.Id });
        await Plans(w).Deactivate(w.OrganizationId, w.ActorUserId, silver.Id);

        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));

        ClientMembershipDto renewed = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal(gold.Id, renewed.MembershipPlanId);
        Assert.Null(renewed.PendingChange);
        Assert.Equal(ErrorCodes.MembershipPlanInactive, renewed.PlanUpdateNotApplied.Reason);
        Assert.Equal(2, (await Memberships(w).GetPeriods(w.OrganizationId, sold.Id)).Count);
    }

    [Fact]
    public async Task PlanDeactivation_EndsMembershipsAtTheRenewal_UnlessReactivatedBefore()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PlanDeactivation_EndsMembershipsAtTheRenewal_UnlessReactivatedBefore));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, gold.Id);

        MembershipPlanDto deactivated = await Plans(w).Deactivate(w.OrganizationId, w.ActorUserId, gold.Id);
        Assert.Equal(WarningCodes.MembershipPlanMembershipsEnding, Assert.Single(deactivated.Warnings).Code);
        Assert.Equal(sold.Id, Assert.Single(await Memberships(w).GetEndingDueToPlanDeactivation(w.OrganizationId, gold.Id)).Id);

        // Reactivated before the renewal: the membership simply continues.
        await Plans(w).Activate(w.OrganizationId, w.ActorUserId, gold.Id);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));
        Assert.Null((await Memberships(w).GetById(w.OrganizationId, sold.Id)).EndsOn);

        await Plans(w).Deactivate(w.OrganizationId, w.ActorUserId, gold.Id);
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(2));
        ClientMembershipDto ended = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal((Today.AddMonths(2).AddDays(-1), MembershipEndReason.PlanDeactivated), (ended.EndsOn.Value, ended.EndReason.Value));
        Assert.Equal(2, (await Memberships(w).GetPeriods(w.OrganizationId, sold.Id)).Count);
    }

    [Fact]
    public async Task AutoEndAfterUnpaidPeriods_EndsAtTheLastOpenPeriod_AndTheDebtStays()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AutoEndAfterUnpaidPeriods_EndsAtTheLastOpenPeriod_AndTheDebtStays));
        await w.Resolve<IOrganizationSettingsService>().UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 7, DebtBehavior = MembershipDebtBehavior.StopCovering, AutoEndAfterUnpaidPeriods = 1 });
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, gold.Id);

        // Off by default it would just renew; with N = 1 the unpaid first period (grace over) ends it at the renewal.
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));

        ClientMembershipDto ended = await Memberships(w).GetById(w.OrganizationId, sold.Id);
        Assert.Equal((Today.AddMonths(1).AddDays(-1), MembershipEndReason.NonPayment), (ended.EndsOn.Value, ended.EndReason.Value));
        MembershipChargeDto debt = Assert.Single(await Charges(w, sold.Id));
        Assert.Equal((MembershipChargeStatus.Pending, 50m), (debt.Status, debt.OutstandingAmount));
    }

    [Fact]
    public async Task Charges_ArePaidThroughCheckout_PartiallyAndInFull_WithTheProjectionKeptInSync()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Charges_ArePaidThroughCheckout_PartiallyAndInFull_WithTheProjectionKeptInSync));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, gold.Id);
        Guid chargeId = Assert.Single(await Charges(w, sold.Id)).Id;

        CheckoutDto checkout = await Checkouts(w).Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        checkout = await Checkouts(w).AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = chargeId, Amount = 20m });
        Assert.Equal(chargeId, Assert.Single(checkout.Items).MembershipChargeId);

        // The charge cannot be in two open checkouts, nor written off while it is in one.
        CheckoutDto second = await Checkouts(w).Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        Assert.Equal(ErrorCodes.MembershipChargeInOpenCheckout, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Checkouts(w).AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, second.Id,
                new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = chargeId }))).Code);

        await Checkouts(w).RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest { Amount = 20m, Method = PaymentMethod.Cash });
        await Checkouts(w).Complete(w.OrganizationId, w.ActorUserId, checkout.Id);
        MembershipChargeDto partial = Assert.Single(await Charges(w, sold.Id));
        Assert.Equal((MembershipChargeStatus.PartiallyPaid, 20m, 30m, false), (partial.Status, partial.SettledAmount, partial.OutstandingAmount, partial.InOpenCheckout));

        // The rest goes through the second checkout (default amount = the remaining debt).
        await Checkouts(w).AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, second.Id, new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = chargeId });
        await Checkouts(w).RecordPayment(w.OrganizationId, w.ActorUserId, second.Id, new CheckoutPaymentCreateRequest { Amount = 30m, Method = PaymentMethod.Card });
        await Checkouts(w).Complete(w.OrganizationId, w.ActorUserId, second.Id);

        Assert.Equal(MembershipChargeStatus.Paid, Assert.Single(await Charges(w, sold.Id)).Status);
        Assert.Equal((MembershipStanding.Current, 0m), ((await Memberships(w).GetById(w.OrganizationId, sold.Id)).Standing, (await Memberships(w).GetById(w.OrganizationId, sold.Id)).OutstandingAmount));

        await using DatabaseContext db = w.NewDb();
        var projection = await db.MembershipCharges.AsNoTracking().SingleAsync(c => c.Id == chargeId);
        Assert.Equal((50m, MembershipChargeSettlementStatus.Paid), (projection.SettledAmount, projection.SettlementStatus));

        // A paid sale can no longer be voided (Q24.4).
        Assert.Equal(ErrorCodes.MembershipSaleHasPayments, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Memberships(w).VoidSale(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipVoidRequest { Reason = "x" }))).Code);
    }

    [Fact]
    public async Task VoidedPayment_InTheOpenCheckout_ReturnsTheChargeToUnpaid()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(VoidedPayment_InTheOpenCheckout_ReturnsTheChargeToUnpaid));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, gold.Id);
        Guid chargeId = Assert.Single(await Charges(w, sold.Id)).Id;

        CheckoutDto checkout = await Checkouts(w).Create(w.OrganizationId, w.ActorUserId,
            new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });
        await Checkouts(w).AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = chargeId });
        CheckoutDto paid = await Checkouts(w).RecordPayment(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutPaymentCreateRequest { Amount = 50m, Method = PaymentMethod.Cash });
        Assert.Equal(MembershipChargeStatus.Paid, Assert.Single(await Charges(w, sold.Id)).Status);

        await Checkouts(w).VoidPayment(w.OrganizationId, w.ActorUserId, checkout.Id, Assert.Single(paid.Payments).Id,
            new CheckoutPaymentVoidRequest { Reason = "Pogrešna uplata" });
        Assert.Equal(MembershipChargeStatus.Pending, Assert.Single(await Charges(w, sold.Id)).Status);
        await using DatabaseContext db = w.NewDb();
        Assert.Equal(MembershipChargeSettlementStatus.Unpaid, (await db.MembershipCharges.AsNoTracking().SingleAsync(c => c.Id == chargeId)).SettlementStatus);
    }

    [Fact]
    public async Task WriteOff_MakesTheChargeFinal_AndAnUnpaidSaleCanStillBeVoided()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WriteOff_MakesTheChargeFinal_AndAnUnpaidSaleCanStillBeVoided));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m, startFee: 10m);
        ClientMembershipDto sold = await Sell(w, gold.Id);
        MembershipChargeDto startFee = (await Charges(w, sold.Id)).Single(c => c.Kind == MembershipChargeKind.StartFee);

        MembershipChargeDto writtenOff = await Memberships(w).WriteOffCharge(w.OrganizationId, w.ActorUserId, startFee.Id,
            new MembershipChargeWriteOffRequest { Reason = "Akcija" });
        Assert.Equal((MembershipChargeStatus.WrittenOff, 0m, "Akcija"), (writtenOff.Status, writtenOff.OutstandingAmount, writtenOff.WriteOffReason));
        Assert.Equal(ErrorCodes.MembershipChargeNotOpen, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Memberships(w).WriteOffCharge(w.OrganizationId, w.ActorUserId, startFee.Id, new MembershipChargeWriteOffRequest { Reason = "x" }))).Code);

        ClientMembershipDto voided = await Memberships(w).VoidSale(w.OrganizationId, w.ActorUserId, sold.Id, new ClientMembershipVoidRequest { Reason = "Greška" });
        Assert.Equal(MembershipState.Voided, voided.State);

        // Review 2C (#6): every charge — the written-off one too — is voided, and the write-off stays in its history.
        List<MembershipChargeDto> charges = await Charges(w, sold.Id);
        Assert.All(charges, c => Assert.Equal(MembershipChargeStatus.Voided, c.Status));
        MembershipChargeDto voidedStartFee = charges.Single(c => c.Kind == MembershipChargeKind.StartFee);
        Assert.Equal("Akcija", voidedStartFee.WriteOffReason);
        Assert.NotNull(voidedStartFee.WrittenOffAt);
    }

    [Fact]
    public async Task AFreePlanWithAStartFee_ChargesOnlyTheStartFee()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AFreePlanWithAStartFee_ChargesOnlyTheStartFee));
        MembershipPlanDto free = await Plan(w, "Free", 0m, startFee: 15m);

        ClientMembershipDto sold = await Sell(w, free.Id);

        MembershipChargeDto charge = Assert.Single(await Charges(w, sold.Id));
        Assert.Equal((MembershipChargeKind.StartFee, 15m), (charge.Kind, charge.Amount));
        Assert.Single(await Memberships(w).GetPeriods(w.OrganizationId, sold.Id));
    }

    [Fact]
    public async Task ChangingPlan_DoesNotEscapeTheMinimumCommitment_TheLaterEndWins()
    {
        // Review 2C (#8): commitment end = later of (the previous commitment end, change date + the new plan's commitment).
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangingPlan_DoesNotEscapeTheMinimumCommitment_TheLaterEndWins));
        MembershipPlanDto strict = await PlanWithCommitment(w, "Strict", 3);
        MembershipPlanDto loose = await PlanWithCommitment(w, "Loose", 1);
        MembershipPlanDto longer = await PlanWithCommitment(w, "Longer", 6);

        ClientMembershipDto first = await Sell(w, strict.Id);
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, first.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = loose.Id });
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(1));
        Assert.Equal(loose.Id, (await Memberships(w).GetById(w.OrganizationId, first.Id)).MembershipPlanId);

        // The old 3-period commitment still binds (the new plan alone would end after month 2).
        MembershipCancellationPreviewDto keptOld = await Memberships(w).PreviewCancellation(w.OrganizationId, first.Id);
        Assert.Equal((Today.AddMonths(3).AddDays(-1), MembershipEndEffectiveReason.MinimumCommitment), (keptOld.EffectiveOn, keptOld.Reason));

        // A longer new commitment counts from the change date.
        await Memberships(w).ChangePlan(w.OrganizationId, w.ActorUserId, first.Id, new ClientMembershipPlanChangeRequest { MembershipPlanId = longer.Id });
        await Renewal(w).RunForOrganizationOn(w.OrganizationId, Today.AddMonths(2));
        MembershipCancellationPreviewDto newer = await Memberships(w).PreviewCancellation(w.OrganizationId, first.Id);
        Assert.Equal((Today.AddMonths(8).AddDays(-1), MembershipEndEffectiveReason.MinimumCommitment), (newer.EffectiveOn, newer.Reason));
    }

    private static async Task<MembershipPlanDto> PlanWithCommitment(SchedulingWorld w, string name, int commitment) =>
        await Plans(w).Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = name, Price = 40m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies, MinimumCommitmentPeriods = commitment,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });

    [Fact]
    public async Task Delinquency_BlocksPauses_AndAPauseProlongsTheCurrentPeriodRow()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delinquency_BlocksPauses_AndAPauseProlongsTheCurrentPeriodRow));
        MembershipPlanDto gold = await Plan(w, "Gold", 50m);
        ClientMembershipDto sold = await Sell(w, gold.Id);

        ClientMembershipDto paused = await Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
            new ClientMembershipPauseRequest { StartsOn = Today.AddDays(2), EndsOn = Today.AddDays(4) });
        Assert.Equal(Today.AddMonths(1).AddDays(2), Assert.Single(await Memberships(w).GetPeriods(w.OrganizationId, sold.Id)).EndsOn);
        await Memberships(w).CancelPause(w.OrganizationId, w.ActorUserId, sold.Id, Assert.Single(paused.Pauses).Id);
        Assert.Equal(Today.AddMonths(1).AddDays(-1), Assert.Single(await Memberships(w).GetPeriods(w.OrganizationId, sold.Id)).EndsOn);

        // Make the first charge overdue beyond the default 7-day grace.
        await using (DatabaseContext db = w.NewDb())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dunelight.membership_charges SET due_on = {Today.AddDays(-10)} WHERE client_membership_id = {sold.Id}");

        Assert.Equal(MembershipStanding.Delinquent, (await Memberships(w).GetById(w.OrganizationId, sold.Id)).Standing);
        Assert.Equal(ErrorCodes.MembershipDelinquent, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Memberships(w).Pause(w.OrganizationId, w.ActorUserId, sold.Id,
                new ClientMembershipPauseRequest { StartsOn = Today.AddDays(2), EndsOn = Today.AddDays(4) }))).Code);
    }

    [Fact]
    public async Task DebtRules_AreValidated_AndReturnedInTheSettings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DebtRules_AreValidated_AndReturnedInTheSettings));
        IOrganizationSettingsService settings = w.Resolve<IOrganizationSettingsService>();

        OrganizationSettingsDto defaults = await settings.GetSettings(w.OrganizationId);
        Assert.Equal((7, MembershipDebtBehavior.StopCovering, (int?)null),
            (defaults.MembershipGraceDays, defaults.MembershipDebtBehavior, defaults.MembershipAutoEndAfterUnpaidPeriods));

        await Assert.ThrowsAsync<ValidationAppException>(() => settings.UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 7, DebtBehavior = MembershipDebtBehavior.KeepCovering, AutoEndAfterUnpaidPeriods = 0 }));
        OrganizationSettingsDto updated = await settings.UpdateMembershipDebtRules(w.OrganizationId, w.ActorUserId,
            new OrganizationMembershipDebtUpdateRequest { GraceDays = 3, DebtBehavior = MembershipDebtBehavior.BlockBooking, AutoEndAfterUnpaidPeriods = 2 });
        Assert.Equal((3, MembershipDebtBehavior.BlockBooking, (int?)2),
            (updated.MembershipGraceDays, updated.MembershipDebtBehavior, updated.MembershipAutoEndAfterUnpaidPeriods));
    }
}
