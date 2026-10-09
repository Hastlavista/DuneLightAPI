#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2F — commissions aligned with the Vagaro model (ADR-0030, decision record 2026-10-08): the base is the session price
/// (manual amount or list price) unless "subtract discounts" / "subtract membership discounts" (a fully covered session → 0); a
/// package-covered session keeps the session price; the employee's service rule beats the general rule (one tier, no threshold) and
/// "no commission" excludes a service explicitly; every commission explains which rule applied and which did not; rule history with
/// deactivation that never falls back; Q38 on a fully paid P1 fee; sale commission for the employee chosen on the item (active when
/// chosen), Q50 correction, assignment afterwards when there was no recipient; the first membership sale evaluated once; event-based
/// reporting.
/// </summary>
public class CommissionVagaroTests
{
    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(TestClock.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);
    private static int LateWindow() => (int)(SchedulingWorld.Future(10) - TestClock.UtcNow).TotalMinutes + 60;

    private static ICommissionRuleService Rules(SchedulingWorld w) => w.Resolve<ICommissionRuleService>();
    private static ICommissionService Ledger(SchedulingWorld w) => w.Resolve<ICommissionService>();

    private static Task<CommissionRuleDto> ServiceRule(SchedulingWorld w, Employee employee, CommissionCalculationType type, decimal value,
        DateOnly? effectiveFrom = null, ServiceEntity service = null) =>
        Rules(w).Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = employee.Id.Value, SubjectType = CommissionSubjectType.Service, ServiceId = (service ?? w.Service).Id,
            CalculationType = type, Value = value, EffectiveFrom = effectiveFrom
        });

    private static Task<CommissionRuleDto> GeneralRule(SchedulingWorld w, Employee employee, CommissionCalculationType type, decimal value) =>
        Rules(w).Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = employee.Id.Value, SubjectType = CommissionSubjectType.AllServices,
            Tiers = new List<CommissionRuleTierDto> { new() { FromRevenue = 0m, CalculationType = type, Value = value } }
        });

    private static Task SaleRule(SchedulingWorld w, Employee employee, CommissionSubjectType subject, Guid subjectId, CommissionCalculationType type, decimal value) =>
        Rules(w).Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = employee.Id.Value, SubjectType = subject,
            PackageId = subject == CommissionSubjectType.Package ? subjectId : null,
            MembershipPlanId = subject == CommissionSubjectType.MembershipPlan ? subjectId : null,
            CalculationType = type, Value = value
        });

    private static Task Settings(SchedulingWorld w, bool discounts = false, bool membershipDiscounts = false,
        CommissionLateCancellationMode late = CommissionLateCancellationMode.Never) =>
        w.Resolve<IOrganizationSettingsService>().UpdateCommissionSettings(w.OrganizationId, w.ActorUserId, new OrganizationCommissionSettingsDto
        {
            DeductDiscounts = discounts, DeductMembershipDiscounts = membershipDiscounts, LateCancellation = late
        });

    /// <summary>The acting user works as the world's employee (default sale commission recipient / seller).</summary>
    private static async Task ActorIsEmployee(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        Employee employee = await db.Employees.SingleAsync(e => e.Id == w.Employee.Id);
        employee.UserId = w.ActorUserId;
        await db.SaveChangesAsync();
    }

    private static async Task SetEmployeeActive(SchedulingWorld w, Employee employee, bool isActive)
    {
        await using DatabaseContext db = w.NewDb();
        Employee row = await db.Employees.SingleAsync(e => e.Id == employee.Id);
        row.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private static CommissionRuleEvaluationDto Evaluation(CommissionEntry entry) =>
        JsonSerializer.Deserialize<CommissionRuleEvaluationDto>(entry.RuleEvaluation);

    private static async Task<Guid> Book(SchedulingWorld w, int days) =>
        (await w.CreateAppointment(w.CreateRequest(At(days, 10), overrideAvailability: true))).Id;

    private static Task<MembershipPlanDto> Plan(SchedulingWorld w, decimal price, decimal startFee = 0m, bool coversService = true,
        params MembershipPriceBenefitDto[] benefits) =>
        w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = $"Plan-{Guid.NewGuid():N}", Price = price, StartFee = startFee, BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate, CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = coversService ? new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } } : new(),
            PriceBenefits = benefits.ToList(),
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });

    private static Task<ClientMembershipDto> Sell(SchedulingWorld w, MembershipPlanDto plan, Client client = null) =>
        w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, (client ?? w.Client).Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });

    private static Task<CheckoutDto> OpenCheckout(SchedulingWorld w) =>
        w.Checkouts.Create(w.OrganizationId, w.ActorUserId, new CheckoutCreateRequest { ClientId = w.Client.Id.Value, CompanyId = w.Company.Id.Value });

    private static Task<CheckoutDto> Pay(SchedulingWorld w, Guid checkoutId, decimal amount) =>
        w.Checkouts.RecordPayment(w.OrganizationId, w.ActorUserId, checkoutId, new CheckoutPaymentCreateRequest { Amount = amount, Method = PaymentMethod.Cash });

    private static async Task PayChargesAndComplete(SchedulingWorld w, IEnumerable<MembershipChargeDto> charges)
    {
        List<MembershipChargeDto> list = charges.ToList();
        CheckoutDto checkout = await OpenCheckout(w);
        foreach (MembershipChargeDto charge in list)
            await w.Checkouts.AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, checkout.Id,
                new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = charge.Id });
        await Pay(w, checkout.Id, list.Sum(c => c.Amount));
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);
    }

    #region Base (Vagaro switches)

    [Fact]
    public async Task Base_WithTheSwitchesOff_IsTheSessionPrice_ListPriceOrAManualAmountEvenAboveIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Base_WithTheSwitchesOff_IsTheSessionPrice_ListPriceOrAManualAmountEvenAboveIt));
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9)));
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(11), settlementAmount: 70m));

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.BaseAmount).ToList();
        Assert.Equal(new[] { (50m, 5m, false), (70m, 7m, true) }, entries.Select(e => (e.BaseAmount, e.CommissionAmount, e.IsManualPrice.Value)));
        Assert.All(entries, e => Assert.Equal((CommissionPaymentSource.Direct, false, false, CommissionRuleScope.Subject),
            (e.PaymentSource.Value, e.DeductDiscounts.Value, e.DeductMembershipDiscounts.Value, e.AppliedRuleScope.Value)));
        Assert.Equal(50m, entries[1].ListPriceAmount); // the list price is kept next to the manual price
    }

    [Fact]
    public async Task Base_MembershipCoveredSession_IsTheSessionPrice_UnlessMembershipDiscountsAreSubtracted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Base_MembershipCoveredSession_IsTheSessionPrice_UnlessMembershipDiscountsAreSubtracted));
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 20m);
        ClientMembershipDto membership = await Sell(w, await Plan(w, 50m));
        Guid first = await Book(w, 1);
        Guid second = await Book(w, 2);

        await w.SetBookingStatus(first, w.Client, BookingStatus.Completed, isPaid: false);
        await Settings(w, membershipDiscounts: true);
        await w.SetBookingStatus(second, w.Client, BookingStatus.Completed, isPaid: false);

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.EarnedAt).ToList();
        Assert.Equal(new[] { (50m, 10m, false), (0m, 0m, true) }, entries.Select(e => (e.BaseAmount, e.CommissionAmount, e.DeductMembershipDiscounts.Value)));
        Assert.All(entries, e => Assert.Equal((CommissionPaymentSource.Membership, (Guid?)membership.Id), (e.PaymentSource.Value, e.CoverageSourceId)));
    }

    [Fact]
    public async Task Base_PackageCoveredSession_IsTheSessionPrice_ThePackageIsAPaymentMethod()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Base_PackageCoveredSession_IsTheSessionPrice_ThePackageIsAPaymentMethod));
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        await Settings(w, discounts: true, membershipDiscounts: true);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1)); // 100 € / 5 entries

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionPaymentSource.Package, package.Id, 50m, 5m), (entry.PaymentSource.Value, entry.CoverageSourceId, entry.BaseAmount, entry.CommissionAmount));
    }

    [Fact]
    public async Task Base_TheMemberPriceIsSubtractedOnlyBySubtractMembershipDiscounts_NotByTheGeneralDiscountSwitch()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Base_TheMemberPriceIsSubtractedOnlyBySubtractMembershipDiscounts_NotByTheGeneralDiscountSwitch));
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        await Sell(w, await Plan(w, 30m, 0m, false,
            new MembershipPriceBenefitDto { Scope = MembershipPriceBenefitScope.AllServices, Type = MembershipPriceBenefitType.PercentOff, Value = 20m }));
        Guid off = await Book(w, 1), discounts = await Book(w, 2), membershipDiscounts = await Book(w, 3);

        await w.SetBookingStatus(off, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);
        await Settings(w, discounts: true);
        await w.SetBookingStatus(discounts, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);
        await Settings(w, membershipDiscounts: true);
        await w.SetBookingStatus(membershipDiscounts, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);

        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(50m, Assert.Single(entries, e => e.AppointmentId == off).BaseAmount);
        Assert.Equal(50m, Assert.Single(entries, e => e.AppointmentId == discounts).BaseAmount); // a member price is not a general discount
        CommissionEntry member = Assert.Single(entries, e => e.AppointmentId == membershipDiscounts);
        Assert.Equal((40m, 4m), (member.BaseAmount, member.CommissionAmount));
    }

    #endregion

    #region Rules: precedence, "no commission", explanation, history

    [Fact]
    public async Task GeneralRule_CoversEveryIndividualService_TheServiceRuleWins_NoCommissionExcludes_AndEachCommissionExplainsTheChoice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GeneralRule_CoversEveryIndividualService_TheServiceRuleWins_NoCommissionExcludes_AndEachCommissionExplainsTheChoice));
        ServiceEntity other = await w.AddService(30, 20m, name: "Other");
        ServiceEntity excluded = await w.AddService(30, 30m, name: "Excluded");
        await w.AssignEmployeeToService(w.Employee, other);
        await w.AssignEmployeeToService(w.Employee, excluded);
        CommissionRuleDto general = await GeneralRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        CommissionRuleDto specific = await ServiceRule(w, w.Employee, CommissionCalculationType.Fixed, 7m);
        await ServiceRule(w, w.Employee, CommissionCalculationType.None, 0m, service: excluded);
        CommissionRuleTierDto tier = Assert.Single(general.Tiers);
        Assert.Equal((0m, CommissionCalculationType.Percentage, 10m), (tier.FromRevenue, tier.CalculationType, tier.Value));

        Guid bySpecific = (await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9)))).Id;
        Guid byGeneral = (await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(11), service: other))).Id;
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(13), service: excluded));

        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(2, entries.Count); // "no commission" earns nothing and the general rule does not step in
        CommissionEntry fromSpecific = Assert.Single(entries, e => e.AppointmentId == bySpecific);
        Assert.Equal((CommissionRuleScope.Subject, specific.Id, 7m), (fromSpecific.AppliedRuleScope.Value, fromSpecific.CommissionRuleId, fromSpecific.CommissionAmount));
        CommissionRuleEvaluationItemDto skipped = Assert.Single(Evaluation(fromSpecific).NotApplied);
        Assert.Equal((general.Id, CommissionRuleScope.AllServices), (skipped.RuleId, skipped.Scope));
        Assert.Contains("prednost", skipped.Reason);

        CommissionEntry fromGeneral = Assert.Single(entries, e => e.AppointmentId == byGeneral);
        Assert.Equal((CommissionRuleScope.AllServices, general.Id, 2m), (fromGeneral.AppliedRuleScope.Value, fromGeneral.CommissionRuleId, fromGeneral.CommissionAmount));
        CommissionEntryDto listed = (await Ledger(w).GetEntries(w.OrganizationId, new CommissionEntryQuery { From = SchedulingWorld.Day(TestClock.UtcNow), To = SchedulingWorld.Day(TestClock.UtcNow) } /* CHANGED in T1: From/To su dani organizacije (DateOnly, oba uključena) */))
            .Items.Single(e => e.Id == fromGeneral.Id);
        Assert.Equal(general.Id, listed.RuleEvaluation.Applied.RuleId);
        Assert.Empty(listed.RuleEvaluation.NotApplied);

        await Assert.ThrowsAsync<ValidationAppException>(() => Rules(w).Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = w.Employee.Id.Value, SubjectType = CommissionSubjectType.AllServices, EffectiveFrom = new DateOnly(2030, 1, 1),
            Tiers = new List<CommissionRuleTierDto> { new() { FromRevenue = 1000m, CalculationType = CommissionCalculationType.Percentage, Value = 5m } }
        })); // revenue tiers come with the Payroll phase
    }

    [Fact]
    public async Task DeactivatedServiceRule_TheGeneralRuleAppliesFromThatDate_AndTheResponseWarnsAboutIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DeactivatedServiceRule_TheGeneralRuleAppliesFromThatDate_AndTheResponseWarnsAboutIt));
        CommissionRuleDto general = await GeneralRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        CommissionRuleDto specific = await ServiceRule(w, w.Employee, CommissionCalculationType.Fixed, 7m);

        CommissionRuleDto deactivated = await Rules(w).SetActive(w.OrganizationId, w.ActorUserId, specific.Id, false);
        AppointmentDto later = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(later.Id, w.CompleteRequest(SchedulingWorld.Future(10)));

        WarningDto warning = Assert.Single(deactivated.Warnings);
        WarningCommissionGeneralRuleAppliesDetails details = Assert.IsType<WarningCommissionGeneralRuleAppliesDetails>(warning.Details);
        Assert.Equal((WarningCodes.CommissionServiceRuleGeneralApplies, general.Id, DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime), 10m),
            (warning.Code, details.GeneralRuleId, details.EffectiveOn, details.Value));
        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionRuleScope.AllServices, 5m), (entry.AppliedRuleScope.Value, entry.CommissionAmount));
        Assert.Contains(Evaluation(entry).NotApplied, n => n.RuleId == specific.Id && n.Reason.Contains("deaktivirano"));
    }

    [Fact]
    public async Task RuleHistory_TheVersionOnTheSessionDateApplies_DeactivationNeverFallsBack_AndOnlyAnUnusedVersionCanBeDeleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RuleHistory_TheVersionOnTheSessionDateApplies_DeactivationNeverFallsBack_AndOnlyAnUnusedVersionCanBeDeleted));
        DateOnly secondVersionFrom = DateOnly.FromDateTime(SchedulingWorld.FutureDay.Date);
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        CommissionRuleDto second = await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 20m, secondVersionFrom);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));
        AppointmentDto future = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.CompleteParticipations(future.Id, w.CompleteRequest(SchedulingWorld.Future(10)));
        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(5m, Assert.Single(entries, e => e.AppointmentId != future.Id).CommissionAmount);
        Assert.Equal(10m, Assert.Single(entries, e => e.AppointmentId == future.Id).CommissionAmount);

        CommissionRuleDto deactivated = await Rules(w).SetActive(w.OrganizationId, w.ActorUserId, second.Id, false);
        Assert.Equal(DateOnly.FromDateTime(TestClock.UtcNow.UtcDateTime), deactivated.DeactivatedFrom);
        AppointmentDto later = await w.CreateAppointment(SchedulingWorld.Future(12));
        await w.CompleteParticipations(later.Id, w.CompleteRequest(SchedulingWorld.Future(12)));
        Assert.DoesNotContain(await w.LoadCommissionEntries(), e => e.AppointmentId == later.Id); // no fallback to the 10 % version

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => Rules(w).Delete(w.OrganizationId, second.Id));
        CommissionRuleDto mistake = await ServiceRule(w, w.Employee, CommissionCalculationType.Fixed, 3m, secondVersionFrom.AddYears(1));
        await Rules(w).Delete(w.OrganizationId, mistake.Id); // an unused version can be deleted
        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionRuleAlreadyExists,
            () => ServiceRule(w, w.Employee, CommissionCalculationType.Fixed, 3m, secondVersionFrom)); // a deactivated version keeps its date
        await Assert.ThrowsAsync<ValidationAppException>(() => Rules(w).Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = w.Employee.Id.Value, SubjectType = CommissionSubjectType.Service, ServiceId = w.Service.Id,
            Kind = CommissionRuleKind.Sale, CalculationType = CommissionCalculationType.Fixed, Value = 1m
        }));
    }

    #endregion

    #region Q38 — commission on a paid P1 fee

    private static async Task<(Guid ParticipationId, Guid CheckoutId)> LateCancelWithFeeInCheckout(SchedulingWorld w)
    {
        await w.PublishDefaultPolicyVersion(LateWindow(), CancellationFeeType.Fixed, 20m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "late");
        Guid participationId = created.Bookings.Single().Participations.Single().Id;
        CheckoutDto checkout = await OpenCheckout(w);
        await w.Checkouts.AddBookingItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddBookingItemRequest { ParticipationId = participationId });
        return (participationId, checkout.Id);
    }

    [Fact]
    public async Task PolicyFee_ByDefault_APaidFeeEarnsNothing()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PolicyFee_ByDefault_APaidFeeEarnsNothing));
        await ServiceRule(w, w.Employee, CommissionCalculationType.Percentage, 10m);
        (_, Guid checkoutId) = await LateCancelWithFeeInCheckout(w);

        await Pay(w, checkoutId, 20m);

        Assert.Empty(await w.LoadCommissionEntries());
    }

    [Fact]
    public async Task PolicyFee_WhenFeePaid_EarnsOnFullPayment_FixedCappedAtTheFee_ReversedByAVoidedPaymentAndAWaiver()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PolicyFee_WhenFeePaid_EarnsOnFullPayment_FixedCappedAtTheFee_ReversedByAVoidedPaymentAndAWaiver));
        await Settings(w, late: CommissionLateCancellationMode.WhenFeePaid);
        await ServiceRule(w, w.Employee, CommissionCalculationType.Fixed, 30m);
        (Guid participationId, Guid checkoutId) = await LateCancelWithFeeInCheckout(w);

        await Pay(w, checkoutId, 10m);
        Assert.Empty(await w.LoadCommissionEntries()); // partially paid fee earns nothing

        CheckoutDto paid = await Pay(w, checkoutId, 10m);
        CommissionEntry earned = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionSourceType.PolicyFee, 20m, 20m, true, CommissionPaymentSource.Direct),
            (earned.SourceType, earned.BaseAmount, earned.CommissionAmount, earned.WasCapped, earned.PaymentSource.Value));

        Guid secondPayment = paid.Payments.OrderByDescending(p => p.CreatedAt).First().Id;
        await w.Checkouts.VoidPayment(w.OrganizationId, w.ActorUserId, checkoutId, secondPayment, new CheckoutPaymentVoidRequest { Reason = "wrong" });
        Assert.Equal(CommissionEntryStatus.Reversed, Assert.Single(await w.LoadCommissionEntries()).Status);

        await Pay(w, checkoutId, 10m);
        Assert.Equal(new[] { (0, CommissionEntryStatus.Reversed), (1, CommissionEntryStatus.Earned) },
            (await w.LoadCommissionEntries()).OrderBy(e => e.SourceVersion).Select(e => (e.SourceVersion, e.Status)));

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyFeeWaive, Grants.AppointmentsWriteAll);
        await w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, participationId, new PolicyConsequenceWaiveRequest { WaiverReason = "goodwill" });
        Assert.All(await w.LoadCommissionEntries(), e => Assert.Equal(CommissionEntryStatus.Reversed, e.Status));
    }

    #endregion

    #region Sale commission ("Sold By"), correction (Q50), assignment afterwards, reporting

    [Fact]
    public async Task SaleCommission_GoesToTheEmployeeChosenOnTheItem_EvenIfLaterInactive_ACorrectionReversesAndReEarns_AndReportsCountEvents()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SaleCommission_GoesToTheEmployeeChosenOnTheItem_EvenIfLaterInactive_ACorrectionReversesAndReEarns_AndReportsCountEvents));
        await ActorIsEmployee(w);
        Employee seller = await w.AddEmployee("Seller");
        Guid packageId = (await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1))).PackageId; // catalog package, 100 €
        await SaleRule(w, w.Employee, CommissionSubjectType.Package, packageId, CommissionCalculationType.Percentage, 5m);
        await SaleRule(w, seller, CommissionSubjectType.Package, packageId, CommissionCalculationType.Percentage, 10m);

        CheckoutDto checkout = await OpenCheckout(w);
        CheckoutItemDto item = (await w.Checkouts.AddPackageItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutAddPackageItemRequest { PackageId = packageId })).Items.Single();
        Assert.Equal(w.Employee.Id, item.SaleCommissionEmployeeId); // default: the employee working the checkout
        await w.Checkouts.SetItemSaleCommissionEmployee(w.OrganizationId, w.ActorUserId, checkout.Id, item.Id,
            new SaleCommissionEmployeeRequest { EmployeeId = seller.Id });
        await SetEmployeeActive(w, seller, false); // chosen while active → still earns
        await Pay(w, checkout.Id, 100m);
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);

        CommissionEntry sale = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((seller.Id.Value, CommissionSourceType.PackageSale, 10m), (sale.EmployeeId, sale.SourceType, sale.CommissionAmount));

        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveEmployee, () => Ledger(w).Reassign(w.OrganizationId, w.ActorUserId, sale.Id.Value,
            new CommissionEntryReassignRequest { EmployeeId = seller.Id, Reason = "x" }));
        CommissionEntryReassignResultDto result = await Ledger(w).Reassign(w.OrganizationId, w.ActorUserId, sale.Id.Value,
            new CommissionEntryReassignRequest { EmployeeId = w.Employee.Id, Reason = "x" });
        Assert.Equal((CommissionEntryStatus.Reversed, CommissionEntryStatus.Earned), (result.Reversed.Status, result.Created.Status));
        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionEntryNotReassignable, () => Ledger(w).Reassign(w.OrganizationId, w.ActorUserId,
            sale.Id.Value, new CommissionEntryReassignRequest { EmployeeId = w.Employee.Id, Reason = "again" }));
        CommissionEntry reversed = (await w.LoadCommissionEntries()).Single(e => e.Id == sale.Id);
        CommissionEntry corrected = (await w.LoadCommissionEntries()).Single(e => e.Id != sale.Id);
        Assert.Equal((CommissionEntryStatus.Reversed, "Korekcija korisnika provizije: x"), (reversed.Status, reversed.ReversalReason));
        Assert.Equal((w.Employee.Id.Value, 5m, 1, sale.Id), (corrected.EmployeeId, corrected.CommissionAmount, corrected.SourceVersion, corrected.CorrectionOfEntryId));

        // Event-based reporting: the reversal is moved to the next period; the earlier period never changes.
        await using (DatabaseContext db = w.NewDb())
        {
            CommissionEntry row = await db.CommissionEntries.SingleAsync(e => e.Id == sale.Id);
            row.ReversedAt = TestClock.UtcNow.AddDays(10);
            await db.SaveChangesAsync();
        }
        // CHANGED in T1: From/To su dani organizacije (DateOnly, oba kraja uključena) umjesto instanata — razdoblja [jučer, danas] i [sutra, +20].
        DateOnly today = SchedulingWorld.Day(TestClock.UtcNow);
        CommissionSummaryResultDto thisPeriod = await Ledger(w).GetSummary(w.OrganizationId, new CommissionSummaryQuery { From = today.AddDays(-1), To = today });
        CommissionSummaryResultDto nextPeriod = await Ledger(w).GetSummary(w.OrganizationId, new CommissionSummaryQuery { From = today.AddDays(1), To = today.AddDays(20) });
        EmployeeCommissionSummaryDto sellerNow = thisPeriod.Employees.Single(e => e.EmployeeId == seller.Id);
        Assert.Equal((10m, 0m, 15m), (sellerNow.EarnedAmount, sellerNow.ReversedAmount, thisPeriod.TotalNetAmount));
        EmployeeCommissionSummaryDto sellerNext = nextPeriod.Employees.Single();
        Assert.Equal((0m, 10m, -10m), (sellerNext.EarnedAmount, sellerNext.ReversedAmount, sellerNext.NetAmount));
        Assert.Equal(-10m, (await Ledger(w).GetEntries(w.OrganizationId, new CommissionEntryQuery { From = today.AddDays(1), To = today.AddDays(20) })).Items.Single().PeriodAmount);
    }

    [Fact]
    public async Task Correction_ToAnEmployeeWithoutARule_WarnsBeforeChangingAnything_AndProceedsOnlyWhenConfirmed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_ToAnEmployeeWithoutARule_WarnsBeforeChangingAnything_AndProceedsOnlyWhenConfirmed));
        await ActorIsEmployee(w);
        Employee withoutRule = await w.AddEmployee("No rule");
        Guid packageId = (await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1))).PackageId;
        await SaleRule(w, w.Employee, CommissionSubjectType.Package, packageId, CommissionCalculationType.Fixed, 8m);
        CheckoutDto checkout = await OpenCheckout(w);
        await w.Checkouts.AddPackageItem(w.OrganizationId, w.ActorUserId, checkout.Id, new CheckoutAddPackageItemRequest { PackageId = packageId });
        await Pay(w, checkout.Id, 100m);
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);
        CommissionEntry sale = Assert.Single(await w.LoadCommissionEntries());

        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionReassignWithoutRule, () => Ledger(w).Reassign(w.OrganizationId, w.ActorUserId,
            sale.Id.Value, new CommissionEntryReassignRequest { EmployeeId = withoutRule.Id, Reason = "x" }));
        Assert.Equal(CommissionEntryStatus.Earned, Assert.Single(await w.LoadCommissionEntries()).Status); // nothing changed

        CommissionEntryReassignResultDto confirmed = await Ledger(w).Reassign(w.OrganizationId, w.ActorUserId, sale.Id.Value,
            new CommissionEntryReassignRequest { EmployeeId = withoutRule.Id, Reason = "x", ConfirmWithoutCommission = true });
        Assert.Equal((CommissionEntryStatus.Reversed, (CommissionEntryDto)null), (confirmed.Reversed.Status, confirmed.Created));
    }

    [Fact]
    public async Task SaleCommission_AnInactiveEmployeeCannotBeChosen_AndWithoutARecipientItCanBeAssignedAfterwardsOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SaleCommission_AnInactiveEmployeeCannotBeChosen_AndWithoutARecipientItCanBeAssignedAfterwardsOnce));
        Employee former = await w.AddEmployee("Former");
        await SetEmployeeActive(w, former, false);
        Guid packageId = (await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1))).PackageId;
        await SaleRule(w, w.Employee, CommissionSubjectType.Package, packageId, CommissionCalculationType.Fixed, 8m);

        CheckoutDto checkout = await OpenCheckout(w); // the acting user is not an employee → no default recipient
        CheckoutItemDto item = (await w.Checkouts.AddPackageItem(w.OrganizationId, w.ActorUserId, checkout.Id,
            new CheckoutAddPackageItemRequest { PackageId = packageId })).Items.Single();
        Assert.Null(item.SaleCommissionEmployeeId);
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveEmployee, () => w.Checkouts.SetItemSaleCommissionEmployee(w.OrganizationId, w.ActorUserId,
            checkout.Id, item.Id, new SaleCommissionEmployeeRequest { EmployeeId = former.Id }));
        await Pay(w, checkout.Id, 100m);
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, checkout.Id);
        Assert.Empty(await w.LoadCommissionEntries());

        CommissionSaleAssignmentResultDto assigned = await Ledger(w).AssignSale(w.OrganizationId, w.ActorUserId,
            new CommissionSaleAssignmentRequest { CheckoutItemId = item.Id, EmployeeId = w.Employee.Id, Reason = "forgot to pick" });
        Assert.Equal((w.Employee.Id.Value, 8m, CommissionSourceType.PackageSale), (assigned.Created.EmployeeId, assigned.Created.CommissionAmount, assigned.Created.SourceType));
        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionSaleNotAssignable, () => Ledger(w).AssignSale(w.OrganizationId, w.ActorUserId,
            new CommissionSaleAssignmentRequest { CheckoutItemId = item.Id, EmployeeId = w.Employee.Id, Reason = "again" }));
    }

    #endregion

    #region First membership sale (Q42)

    [Fact]
    public async Task MembershipSale_EarnsOnceWhenBothChargesAreFinal_ForTheEmployeeOnTheMembership_ThenOnlyACorrection()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MembershipSale_EarnsOnceWhenBothChargesAreFinal_ForTheEmployeeOnTheMembership_ThenOnlyACorrection));
        await ActorIsEmployee(w);
        Employee seller = await w.AddEmployee("Seller");
        MembershipPlanDto plan = await Plan(w, 50m, startFee: 20m);
        await SaleRule(w, seller, CommissionSubjectType.MembershipPlan, plan.Id, CommissionCalculationType.Percentage, 10m);
        ClientMembershipDto sold = await Sell(w, plan);
        Assert.Equal(w.Employee.Id, sold.SaleCommissionEmployeeId); // proposed: the seller running the sale command
        List<MembershipChargeDto> charges = await w.Resolve<IClientMembershipService>().GetCharges(w.OrganizationId, sold.Id);

        await PayChargesAndComplete(w, charges.Where(c => c.Kind == MembershipChargeKind.Period));
        Assert.Empty(await w.LoadCommissionEntries()); // the start fee is still open

        CheckoutDto second = await OpenCheckout(w);
        CheckoutItemDto feeItem = (await w.Checkouts.AddMembershipChargeItem(w.OrganizationId, w.ActorUserId, second.Id,
            new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = charges.Single(c => c.Kind == MembershipChargeKind.StartFee).Id })).Items.Single();
        Assert.Equal((w.Employee.Id, true), (feeItem.SaleCommissionEmployeeId, feeItem.SaleCommissionFromMembership));
        CheckoutDto changed = await w.Checkouts.SetItemSaleCommissionEmployee(w.OrganizationId, w.ActorUserId, second.Id, feeItem.Id,
            new SaleCommissionEmployeeRequest { EmployeeId = seller.Id });
        Assert.Equal(seller.Id, changed.Items.Single().SaleCommissionEmployeeId);
        await Pay(w, second.Id, 20m);
        await w.Checkouts.Complete(w.OrganizationId, w.ActorUserId, second.Id);

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((seller.Id.Value, CommissionSourceType.MembershipSale, sold.Id, 70m, 7m),
            (entry.EmployeeId, entry.SourceType, entry.ClientMembershipId.Value, entry.BaseAmount, entry.CommissionAmount));
        ClientMembershipDto after = await w.Resolve<IClientMembershipService>().GetById(w.OrganizationId, sold.Id);
        Assert.Equal(seller.Id, after.SaleCommissionEmployeeId);
        Assert.NotNull(after.FirstSaleSettledAt);
        await using (DatabaseContext db = w.NewDb())
            Assert.Contains(await db.ClientMembershipAuditLog.Where(a => a.ClientMembershipId == sold.Id).ToListAsync(),
                a => a.ChangeType == "SaleCommissionEmployeeChanged" && a.OldValue == w.Employee.Id.ToString() && a.NewValue == seller.Id.ToString());

        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionSaleAlreadyEarned, () => w.Resolve<IClientMembershipService>()
            .SetSaleCommissionEmployee(w.OrganizationId, w.ActorUserId, sold.Id, new SaleCommissionEmployeeRequest { EmployeeId = w.Employee.Id }));
    }

    [Fact]
    public async Task MembershipSale_WithoutARecipient_IsEvaluatedOnce_AndCanBeAssignedAfterwards_AtTheOriginalBase()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MembershipSale_WithoutARecipient_IsEvaluatedOnce_AndCanBeAssignedAfterwards_AtTheOriginalBase));
        MembershipPlanDto plan = await Plan(w, 50m, startFee: 20m);
        await SaleRule(w, w.Employee, CommissionSubjectType.MembershipPlan, plan.Id, CommissionCalculationType.Percentage, 10m);
        ClientMembershipDto sold = await Sell(w, plan); // the acting user is not an employee → no recipient
        Assert.Null(sold.SaleCommissionEmployeeId);

        await PayChargesAndComplete(w, await w.Resolve<IClientMembershipService>().GetCharges(w.OrganizationId, sold.Id));
        Assert.Empty(await w.LoadCommissionEntries());
        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionSaleAlreadyEvaluated, () => w.Resolve<IClientMembershipService>()
            .SetSaleCommissionEmployee(w.OrganizationId, w.ActorUserId, sold.Id, new SaleCommissionEmployeeRequest { EmployeeId = w.Employee.Id }));

        CommissionSaleAssignmentResultDto assigned = await Ledger(w).AssignSale(w.OrganizationId, w.ActorUserId,
            new CommissionSaleAssignmentRequest { ClientMembershipId = sold.Id, EmployeeId = w.Employee.Id, Reason = "sold by the trainer" });
        Assert.Equal((70m, 7m), (assigned.Created.BaseAmount, assigned.Created.CommissionAmount));
        Assert.Equal(w.Employee.Id, (await w.Resolve<IClientMembershipService>().GetById(w.OrganizationId, sold.Id)).SaleCommissionEmployeeId);
    }

    [Fact]
    public async Task MembershipSale_AWriteOffCompletesTheCondition_WrittenOffCountsZero_AndAllWrittenOffIsFinal()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MembershipSale_AWriteOffCompletesTheCondition_WrittenOffCountsZero_AndAllWrittenOffIsFinal));
        await ActorIsEmployee(w);
        IClientMembershipService memberships = w.Resolve<IClientMembershipService>();
        MembershipPlanDto plan = await Plan(w, 50m, startFee: 20m);
        await SaleRule(w, w.Employee, CommissionSubjectType.MembershipPlan, plan.Id, CommissionCalculationType.Fixed, 15m);

        ClientMembershipDto paidThenWrittenOff = await Sell(w, plan);
        List<MembershipChargeDto> charges = await memberships.GetCharges(w.OrganizationId, paidThenWrittenOff.Id);
        await PayChargesAndComplete(w, charges.Where(c => c.Kind == MembershipChargeKind.Period));
        await memberships.WriteOffCharge(w.OrganizationId, w.ActorUserId, charges.Single(c => c.Kind == MembershipChargeKind.StartFee).Id,
            new MembershipChargeWriteOffRequest { Reason = "promo" });

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((50m, 15m), (entry.BaseAmount, entry.CommissionAmount));

        Client other = await w.AddClient("Other", "Client");
        ClientMembershipDto nothingPaid = await Sell(w, plan, other);
        foreach (MembershipChargeDto charge in await memberships.GetCharges(w.OrganizationId, nothingPaid.Id))
            await memberships.WriteOffCharge(w.OrganizationId, w.ActorUserId, charge.Id, new MembershipChargeWriteOffRequest { Reason = "gift" });

        Assert.Single(await w.LoadCommissionEntries()); // base 0 → no commission
        Assert.NotNull((await memberships.GetById(w.OrganizationId, nothingPaid.Id)).FirstSaleSettledAt);
        await SchedulingAssert.BusinessRule(ErrorCodes.CommissionSaleNotAssignable, () => Ledger(w).AssignSale(w.OrganizationId, w.ActorUserId,
            new CommissionSaleAssignmentRequest { ClientMembershipId = nothingPaid.Id, EmployeeId = w.Employee.Id, Reason = "x" }));
    }

    #endregion
}
