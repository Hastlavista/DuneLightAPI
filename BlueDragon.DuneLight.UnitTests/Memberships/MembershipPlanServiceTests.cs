#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2A — membership plan catalog through the real service against PostgreSQL: immutable versions, active-name
/// uniqueness, reference rules (new references active, unchanged ones grandfathered, other tenants invisible), read-time
/// warnings (Q29.3, Q49) and that plan versions keep services/companies from being hard-deleted.
/// </summary>
public class MembershipPlanServiceTests
{
    private static IMembershipPlanService Plans(SchedulingWorld w) => w.Resolve<IMembershipPlanService>();

    private static MembershipPlanCreateRequest Create(string name, params ServiceEntity[] services)
    {
        MembershipPlanTermsRequest terms = MembershipPlanRulesTests.Terms(limits: new MembershipUsageLimitDto { Window = MembershipUsageWindow.Period, MaxUses = 8 });
        return new MembershipPlanCreateRequest
        {
            Name = name,
            Description = "  Opis  ",
            MaxActiveMemberships = 50,
            Price = terms.Price,
            StartFee = terms.StartFee,
            BillingInterval = terms.BillingInterval,
            RenewalAnchor = terms.RenewalAnchor,
            CompanyScope = terms.CompanyScope,
            Services = services.Select(s => new MembershipPlanCoveredServiceRequest { ServiceId = s.Id }).ToList(),
            UsageLimits = terms.UsageLimits,
            Pause = terms.Pause
        };
    }

    private static MembershipPlanVersionPublishRequest TermsFor(params ServiceEntity[] services) => new()
    {
        Price = 60m,
        StartFee = 0m,
        BillingInterval = MembershipBillingInterval.Monthly,
        RenewalAnchor = MembershipRenewalAnchor.CalendarMonth,
        CompanyScope = MembershipCompanyScope.AllCompanies,
        Services = services.Select(s => new MembershipPlanCoveredServiceRequest { ServiceId = s.Id }).ToList(),
        Pause = new MembershipPauseRulesDto { Allowed = true, MaxPausePeriods = 1 }
    };

    [Fact]
    public async Task Create_PersistsTheFirstVersionWithItsServicesAndLimits()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_PersistsTheFirstVersionWithItsServicesAndLimits));

        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("  Neograničeno  ", w.Service));

        Assert.Equal(("Neograničeno", "Opis", true, 50), (plan.Name, plan.Description, plan.IsActive, plan.MaxActiveMemberships.Value));
        MembershipPlanVersionDto version = Assert.Single(plan.Versions);
        Assert.Equal(1, version.Version);
        Assert.Equal((50m, 10m, MembershipBillingInterval.Monthly, MembershipRenewalAnchor.PurchaseDate),
            (version.Price, version.StartFee, version.BillingInterval, version.RenewalAnchor));
        Assert.Equal(w.Service.Id, Assert.Single(version.Services).ServiceId);
        MembershipUsageLimitDto credits = Assert.Single(version.UsageLimits);
        Assert.Equal((MembershipUsageWindow.Period, 8, (Guid?)null), (credits.Window.Value, credits.MaxUses, credits.ServiceId));
        Assert.Empty(plan.Warnings);

        MembershipPlanDto listed = Assert.Single(await Plans(w).GetAll(w.OrganizationId));
        Assert.Equal(1, listed.LatestVersion.Version);
        Assert.Empty(listed.Versions);
    }

    [Fact]
    public async Task PublishVersion_AddsANewImmutableVersion_AndKeepsTheOldOneUnchanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PublishVersion_AddsANewImmutableVersion_AndKeepsTheOldOneUnchanged));
        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", w.Service));

        MembershipPlanDto published = (await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, TermsFor(w.Service))).Plan;

        Assert.Equal(new[] { 2, 1 }, published.Versions.Select(v => v.Version));
        Assert.Equal((60m, MembershipRenewalAnchor.CalendarMonth), (published.LatestVersion.Price, published.LatestVersion.RenewalAnchor));
        MembershipPlanVersionDto first = published.Versions.Single(v => v.Version == 1);
        Assert.Equal((50m, MembershipRenewalAnchor.PurchaseDate, 1), (first.Price, first.RenewalAnchor, first.UsageLimits.Count));
    }

    [Fact]
    public async Task ActiveNames_AreUniqueCaseInsensitively_AndOnlyAmongActivePlans()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ActiveNames_AreUniqueCaseInsensitively_AndOnlyAmongActivePlans));
        MembershipPlanDto first = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Gold", w.Service));

        BusinessRuleException duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Plans(w).Create(w.OrganizationId, w.ActorUserId, Create(" gold ", w.Service)));
        Assert.Equal(ErrorCodes.DuplicateName, duplicate.Code);

        await Plans(w).Deactivate(w.OrganizationId, w.ActorUserId, first.Id);
        MembershipPlanDto second = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("GOLD", w.Service));
        Assert.True(second.IsActive);

        BusinessRuleException reactivate = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Plans(w).Activate(w.OrganizationId, w.ActorUserId, first.Id));
        Assert.Equal(ErrorCodes.DuplicateName, reactivate.Code);
    }

    [Fact]
    public async Task NewReferences_MustBeActive_ButUnchangedOnesAreGrandfathered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NewReferences_MustBeActive_ButUnchangedOnesAreGrandfathered));
        ServiceEntity other = await w.AddService(60, 30m, name: "Other");
        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", w.Service));

        await w.SetServiceActive(w.Service, false);
        await w.SetServiceActive(other, false);

        // The covered service is now inactive but already in the latest version: still publishable.
        MembershipPlanDto published = (await Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, TermsFor(w.Service))).Plan;
        Assert.False(Assert.Single(published.LatestVersion.Services).IsActive);

        BusinessRuleException added = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Plans(w).PublishVersion(w.OrganizationId, w.ActorUserId, plan.Id, TermsFor(w.Service, other)));
        Assert.Equal(ErrorCodes.InactiveService, added.Code);

        BusinessRuleException created = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("New", other)));
        Assert.Equal(ErrorCodes.InactiveService, created.Code);
    }

    [Fact]
    public async Task AnotherTenantsService_IsNotFound()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnotherTenantsService_IsNotFound));
        await using SchedulingWorld other = await SchedulingWorld.Create(nameof(AnotherTenantsService_IsNotFound) + "_other");

        await Assert.ThrowsAsync<NotFoundAppException>(() =>
            Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", other.Service)));
    }

    [Fact]
    public async Task SelectedCompanies_AllInactive_ShowAWarningOnThePlan()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SelectedCompanies_AllInactive_ShowAWarningOnThePlan));
        MembershipPlanCreateRequest request = Create("Local", w.Service);
        request.CompanyScope = MembershipCompanyScope.SelectedCompanies;
        request.CompanyIds = new List<Guid> { w.Company.Id.Value };

        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, request);
        Assert.Empty(plan.Warnings);
        Assert.Equal(w.Company.Id, Assert.Single(plan.LatestVersion.Companies).CompanyId);

        await w.SetCompanyActive(w.Company, false);
        WarningDto warning = Assert.Single((await Plans(w).GetById(w.OrganizationId, plan.Id)).Warnings);
        Assert.Equal(WarningCodes.MembershipPlanNoActiveCompany, warning.Code);
    }

    [Fact]
    public async Task Capacity_IsAtLeastOne_OrClearedToUnlimited()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Capacity_IsAtLeastOne_OrClearedToUnlimited));
        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", w.Service));

        await Assert.ThrowsAsync<ValidationAppException>(() =>
            Plans(w).UpdateCapacity(w.OrganizationId, w.ActorUserId, plan.Id, new MembershipPlanCapacityRequest { MaxActiveMemberships = 0 }));
        MembershipPlanDto cleared = await Plans(w).UpdateCapacity(w.OrganizationId, w.ActorUserId, plan.Id, new MembershipPlanCapacityRequest());
        Assert.Null(cleared.MaxActiveMemberships);
    }

    [Fact]
    public async Task PlanVersions_KeepServicesAndCompaniesFromBeingHardDeleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PlanVersions_KeepServicesAndCompaniesFromBeingHardDeleted));
        ServiceEntity limited = await w.AddService(60, 30m, name: "Limited");
        Company company = await w.AddCompany("Branch", withWorkingHours: false);
        Assert.False(await w.Resolve<IServiceHandler>().IsReferenced(w.OrganizationId, limited.Id.Value));
        Assert.False(await w.Resolve<ICompanyHandler>().IsReferenced(w.OrganizationId, company.Id.Value));

        MembershipPlanCreateRequest request = Create("Plan", w.Service, limited);
        request.CompanyScope = MembershipCompanyScope.SelectedCompanies;
        request.CompanyIds = new List<Guid> { company.Id.Value };
        await Plans(w).Create(w.OrganizationId, w.ActorUserId, request);

        Assert.True(await w.Resolve<IServiceHandler>().IsReferenced(w.OrganizationId, limited.Id.Value));
        Assert.True(await w.Resolve<ICompanyHandler>().IsReferenced(w.OrganizationId, company.Id.Value));
    }

    [Fact]
    public async Task Schema_RejectsCalendarRenewalOnAYearlyPlan_EvenBypassingTheService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schema_RejectsCalendarRenewalOnAYearlyPlan_EvenBypassingTheService));
        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", w.Service));

        await using DatabaseContext db = w.NewDb();
        db.MembershipPlanVersions.Add(new MembershipPlanVersion
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, MembershipPlanId = plan.Id, Version = 2, Price = 1m,
            BillingInterval = MembershipBillingInterval.Yearly, RenewalAnchor = MembershipRenewalAnchor.CalendarMonth,
            CompanyScope = MembershipCompanyScope.AllCompanies, CreatedAt = TestClock.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Schema_RejectsAnAllowedPauseWithoutAMaximumDuration_EvenBypassingTheService()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schema_RejectsAnAllowedPauseWithoutAMaximumDuration_EvenBypassingTheService));
        MembershipPlanDto plan = await Plans(w).Create(w.OrganizationId, w.ActorUserId, Create("Plan", w.Service));

        await using DatabaseContext db = w.NewDb();
        db.MembershipPlanVersions.Add(new MembershipPlanVersion
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, MembershipPlanId = plan.Id, Version = 2, Price = 1m,
            BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies, PauseAllowed = true, MaxPausesPer12Months = 2,
            CreatedAt = TestClock.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
