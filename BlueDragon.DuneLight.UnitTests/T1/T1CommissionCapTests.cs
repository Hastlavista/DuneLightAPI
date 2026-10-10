#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.T1;

/// <summary>
/// T1-10 (odluka "Tri odluke T1 (2)", dopuna ADR-0030): provizija nikad nije veća od iznosa primljenog za uslugu — izravno
/// naplaćenog ili unaprijed plaćenog kroz paket; jedina iznimka je termin pokriven članarinom uz isključen "oduzmi popuste
/// članstva". Slučaj 1: izravno naplaćena sesija → min(izračunata, naplaćeno), WasCapped + razlog. Slučaj 3: paket → osnovica =
/// plaćena cijena paketa / broj jedinica (T1-11: ograničeno na tu vrijednost sesije; neograničen paket kao članarina). Slučaj 2: članarina uz isključen prekidač → bez ograničenja, a spremanje
/// postavki vraća upozorenje.
/// </summary>
public class T1CommissionCapTests
{
    private static DateTimeOffset At(int days, int hour) => new DateTimeOffset(TestClock.UtcNow.UtcDateTime.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);

    private static Task ServiceRule(SchedulingWorld w, CommissionCalculationType type, decimal value) =>
        w.Resolve<ICommissionRuleService>().Create(w.OrganizationId, w.ActorUserId, new CommissionRuleCreateRequest
        {
            EmployeeId = w.Employee.Id.Value, SubjectType = CommissionSubjectType.Service, ServiceId = w.Service.Id,
            CalculationType = type, Value = value
        });

    private static Task<OrganizationCommissionSettingsDto> Settings(SchedulingWorld w, bool discounts = false, bool membershipDiscounts = false) =>
        w.Resolve<IOrganizationSettingsService>().UpdateCommissionSettings(w.OrganizationId, w.ActorUserId, new OrganizationCommissionSettingsDto
        {
            DeductDiscounts = discounts, DeductMembershipDiscounts = membershipDiscounts, LateCancellation = CommissionLateCancellationMode.Never
        });

    private static CommissionRuleEvaluationDto Evaluation(CommissionEntry entry) =>
        JsonSerializer.Deserialize<CommissionRuleEvaluationDto>(entry.RuleEvaluation);

    private static async Task<ClientMembershipDto> SellCoveringMembership(SchedulingWorld w)
    {
        MembershipPlanDto plan = await w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = $"Plan-{Guid.NewGuid():N}", Price = 50m, StartFee = 0m, BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate, CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            PriceBenefits = new List<MembershipPriceBenefitDto>(),
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        return await w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });
    }

    private static async Task<Guid> Book(SchedulingWorld w, int days) =>
        (await w.CreateAppointment(w.CreateRequest(At(days, 10), overrideAvailability: true))).Id;

    [Fact]
    public async Task DirectlyCharged_FixedRuleAboveTheChargedAmount_IsCappedAtIt_WithTheReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DirectlyCharged_FixedRuleAboveTheChargedAmount_IsCappedAtIt_WithTheReason));
        await ServiceRule(w, CommissionCalculationType.Fixed, 10m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9), settlementAmount: 5m));
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(11), settlementAmount: 0m));
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(13))); // 50 € iz cjenika — 10 € nije iznad naplaćenog

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.SessionPriceAmount).ToList();
        Assert.Equal(new[] { (0m, 0m, true), (5m, 5m, true), (50m, 10m, false) },
            entries.Select(e => (e.SessionPriceAmount.Value, e.CommissionAmount, e.WasCapped)));
        Assert.All(entries, e => Assert.Equal(CommissionPaymentSource.Direct, e.PaymentSource));

        CommissionRuleEvaluationDto five = Evaluation(entries[1]);
        Assert.Equal((5m, "Ograničeno na naplaćeni iznos 5,00 €."), (five.CappedAt.Value, five.CapReason));
        Assert.Equal("Ograničeno na naplaćeni iznos 0,00 €.", Evaluation(entries[0]).CapReason);
        CommissionRuleEvaluationDto full = Evaluation(entries[2]);
        Assert.Null(full.CappedAt);
        Assert.Null(full.CapReason);
        Assert.NotNull(full.Applied); // objašnjenje izbora pravila ostaje
    }

    [Fact]
    public async Task DirectlyCharged_PercentageRule_IsUnaffected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DirectlyCharged_PercentageRule_IsUnaffected));
        await ServiceRule(w, CommissionCalculationType.Percentage, 10m);

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9)));
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(11), settlementAmount: 5m));

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.BaseAmount).ToList();
        Assert.Equal(new[] { (5m, 0.5m, false), (50m, 5m, false) }, entries.Select(e => (e.BaseAmount, e.CommissionAmount, e.WasCapped)));
        Assert.All(entries, e => Assert.Null(Evaluation(e).CapReason));
    }

    [Fact]
    public async Task PackageCovered_TheBaseIsThePaidPackagePricePerUnit_NotTheListPrice_AndIsNotCapped()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackageCovered_TheBaseIsThePaidPackagePricePerUnit_NotTheListPrice_AndIsNotCapped));
        await ServiceRule(w, CommissionCalculationType.Percentage, 10m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1));
        await using (DatabaseContext db = w.NewDb())
        {
            // Paket prodan s popustom: 60 € umjesto 100 € za 5 jedinica → 12 € po jedinici (cjenik sesije je 50 €).
            ClientPackage row = await db.ClientPackages.SingleAsync(p => p.Id == package.Id);
            row.PaidPrice = 60m;
            await db.SaveChangesAsync();
        }

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionPaymentSource.Package, 12m, 12m, 1.2m, 50m, false),
            (entry.PaymentSource.Value, entry.SessionPriceAmount.Value, entry.BaseAmount, entry.CommissionAmount, entry.ListPriceAmount.Value, entry.WasCapped));
        Assert.Equal("Osnovica je plaćena cijena paketa 60,00 € / 5 jedinica = 12,00 €.", Evaluation(entry).BaseNote);
    }

    [Fact]
    public async Task PackageCovered_FixedRule_IsCappedAtTheSessionValueFromThePackage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackageCovered_FixedRule_IsCappedAtTheSessionValueFromThePackage));
        await ServiceRule(w, CommissionCalculationType.Fixed, 30m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1)); // 100 € / 5 = 20 €

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        // CHANGED in T1 (T1-11): fiksno pravilo na paketnoj sesiji ograničeno je na vrijednost sesije iz paketa (prije T1-11:
        // 30 €, bez ograničenja).
        Assert.Equal((20m, 20m, true), (entry.BaseAmount, entry.CommissionAmount, entry.WasCapped));
        Assert.Equal((20m, "Ograničeno na vrijednost sesije iz paketa 20,00 €."), (Evaluation(entry).CappedAt.Value, Evaluation(entry).CapReason));
    }

    [Fact]
    public async Task PackageCovered_FixedTenOnAFiveEuroSession_IsFive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackageCovered_FixedTenOnAFiveEuroSession_IsFive));
        await ServiceRule(w, CommissionCalculationType.Fixed, 10m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateOnly(2035, 1, 1));
        await using (DatabaseContext db = w.NewDb())
        {
            ClientPackage row = await db.ClientPackages.SingleAsync(p => p.Id == package.Id);
            row.PaidPrice = 25m; // 25 € / 5 jedinica = 5 € po sesiji
            await db.SaveChangesAsync();
        }

        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        CommissionEntry entry = Assert.Single(await w.LoadCommissionEntries());
        Assert.Equal((CommissionPaymentSource.Package, 5m, 5m, true), (entry.PaymentSource.Value, entry.BaseAmount, entry.CommissionAmount, entry.WasCapped));
    }

    [Fact]
    public async Task UnlimitedPackage_BehavesLikeAMembership_SwitchOn_Zero_SwitchOff_ListPriceUncapped()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(UnlimitedPackage_BehavesLikeAMembership_SwitchOn_Zero_SwitchOff_ListPriceUncapped));
        await ServiceRule(w, CommissionCalculationType.Fixed, 30m);
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, null, new DateOnly(2035, 1, 1)); // neograničeno

        // Prekidač isključen (zadano): osnovica je cijena sesije (cjenik 50 €), fiksno 30 € bez ograničenja.
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        await Settings(w, membershipDiscounts: true);
        await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(12), clientPackageId: package.Id));

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.EarnedAt).ThenByDescending(e => e.BaseAmount).ToList();
        Assert.All(entries, e => Assert.Equal(CommissionPaymentSource.Package, e.PaymentSource));
        // CHANGED in T1 (T1-11): prije T1-11 neograničen paket je imao osnovicu cijene sesije bez obzira na prekidač.
        CommissionEntry off = entries.Single(e => !e.DeductMembershipDiscounts.Value);
        CommissionEntry on = entries.Single(e => e.DeductMembershipDiscounts.Value);
        Assert.Equal((50m, 30m, false), (off.BaseAmount, off.CommissionAmount, off.WasCapped));
        Assert.Equal((0m, 0m, true), (on.BaseAmount, on.CommissionAmount, on.WasCapped));
        Assert.Contains("Neograničen paket", Evaluation(off).BaseNote);
        Assert.Contains("neograničenim paketom", Evaluation(on).CapReason);
    }

    [Fact]
    public async Task MembershipCovered_SwitchOff_FixedIsNotCapped_SwitchOn_FixedIsCappedAtTheChargedZero()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(MembershipCovered_SwitchOff_FixedIsNotCapped_SwitchOn_FixedIsCappedAtTheChargedZero));
        await ServiceRule(w, CommissionCalculationType.Fixed, 30m);
        await SellCoveringMembership(w);
        Guid first = await Book(w, 1);
        Guid second = await Book(w, 2);

        await w.SetBookingStatus(first, w.Client, BookingStatus.Completed, isPaid: false);
        await Settings(w, membershipDiscounts: true);
        await w.SetBookingStatus(second, w.Client, BookingStatus.Completed, isPaid: false);

        List<CommissionEntry> entries = (await w.LoadCommissionEntries()).OrderBy(e => e.EarnedAt).ToList();
        Assert.All(entries, e => Assert.Equal(CommissionPaymentSource.Membership, e.PaymentSource));
        // Iznimka načela (izričit izbor studija): prekidač isključen → fiksno pravilo u punom iznosu.
        Assert.Equal((30m, false), (entries[0].CommissionAmount, entries[0].WasCapped));
        // CHANGED in T1 (T1-10): uz "oduzmi popuste članstva" pokrivena sesija ima proviziju 0 i za fiksno pravilo (prije: 30 €,
        // jer se fiksni iznos nije ograničavao osnovicom 0 — suprotno ADR-0030 "pokrivena sesija ima proviziju za odrađeno 0").
        Assert.Equal((0m, true), (entries[1].CommissionAmount, entries[1].WasCapped));
    }

    [Fact]
    public async Task SavingSettings_WithMembershipDiscountsOff_WarnsThatMembershipVisitsUseTheListPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SavingSettings_WithMembershipDiscountsOff_WarnsThatMembershipVisitsUseTheListPrice));

        OrganizationCommissionSettingsDto off = await Settings(w, discounts: true, membershipDiscounts: false);
        OrganizationCommissionSettingsDto on = await Settings(w, membershipDiscounts: true);

        Assert.Equal(WarningCodes.CommissionMembershipSessionsAtListPrice, Assert.Single(off.Warnings).Code);
        // T1-11: upozorenje se odnosi i na neograničene pakete (isti kod, Details.AppliesTo).
        Assert.Equal(new[] { "Membership", "UnlimitedPackage" },
            Assert.IsType<WarningCommissionListPriceSessionsDetails>(off.Warnings[0].Details).AppliesTo);
        Assert.Empty(on.Warnings);
        Assert.Empty((await w.Resolve<IOrganizationSettingsService>().GetCommissionSettings(w.OrganizationId)).Warnings);
    }
}
