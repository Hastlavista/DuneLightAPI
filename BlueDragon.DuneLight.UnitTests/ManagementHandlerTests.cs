using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification (same isolated-data + finally-cleanup pattern as RegistrationBootstrapTests)
/// that ManagementHandler's cross-tenant reads (overview/organizations/users) are correct, and that the
/// existing tenant-scoped CompanyHandler stays tenant-isolated alongside it (regression check per task
/// requirement "verify normal tenant handlers/APIs remain tenant-isolated").</summary>
public class ManagementHandlerTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static DatabaseSettings Settings() => new() { ConnectionString = LocalConnectionString };

    private class Seed
    {
        public Guid Org1Id;
        public Guid Org2Id;
        public string Marker;
        public Guid Org1User1Id;
        public Guid Org1User2Id;
        public Guid Org2User1Id;
    }

    private static async Task<Seed> SeedTwoOrganizations()
    {
        string marker = Guid.NewGuid().ToString("N");
        Seed seed = new()
        {
            Marker = marker,
            Org1Id = Guid.NewGuid(),
            Org2Id = Guid.NewGuid(),
            Org1User1Id = Guid.NewGuid(),
            Org1User2Id = Guid.NewGuid(),
            Org2User1Id = Guid.NewGuid()
        };

        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);

        // Organization/Company/User.OrganizationId is a plain scalar FK column, not an EF-modeled navigation
        // (see DatabaseContext), so EF Core has no dependency graph across these types - Organizations must be
        // persisted in their own SaveChangesAsync before anything referencing them, same reasoning as
        // PlatformOperatorHandlerTests.
        context.Organizations.Add(new Organization { Id = seed.Org1Id, Name = $"MgmtTest-Org1-{marker}", Slug = $"mgmt-test-org1-{marker}", CreatedAt = TestClock.UtcNow });
        context.Organizations.Add(new Organization { Id = seed.Org2Id, Name = $"MgmtTest-Org2-{marker}", Slug = $"mgmt-test-org2-{marker}", CreatedAt = TestClock.UtcNow });
        await context.SaveChangesAsync();

        context.Companies.Add(new Company { Id = Guid.NewGuid(), OrganizationId = seed.Org1Id, Name = "Org1-Company-A", Country = "HR", IsActive = true, CreatedAt = TestClock.UtcNow });
        context.Companies.Add(new Company { Id = Guid.NewGuid(), OrganizationId = seed.Org1Id, Name = "Org1-Company-B", Country = "HR", IsActive = true, CreatedAt = TestClock.UtcNow });
        context.Companies.Add(new Company { Id = Guid.NewGuid(), OrganizationId = seed.Org2Id, Name = "Org2-Company-A", Country = "HR", IsActive = true, CreatedAt = TestClock.UtcNow });

        context.Users.Add(new User { Id = seed.Org1User1Id, OrganizationId = seed.Org1Id, Email = $"org1-user1-{marker}@test.local", PasswordHash = "x", ApiKey = Guid.NewGuid().ToString("N"), IsActive = true, CreatedAt = TestClock.UtcNow });
        context.Users.Add(new User { Id = seed.Org1User2Id, OrganizationId = seed.Org1Id, Email = $"org1-user2-{marker}@test.local", PasswordHash = "x", ApiKey = Guid.NewGuid().ToString("N"), IsActive = false, CreatedAt = TestClock.UtcNow });
        context.Users.Add(new User { Id = seed.Org2User1Id, OrganizationId = seed.Org2Id, Email = $"org2-user1-{marker}@test.local", PasswordHash = "x", ApiKey = Guid.NewGuid().ToString("N"), IsActive = true, CreatedAt = TestClock.UtcNow });

        await context.SaveChangesAsync();
        return seed;
    }

    private static async Task Cleanup(Seed seed)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        context.Companies.RemoveRange(context.Companies.Where(c => c.OrganizationId == seed.Org1Id || c.OrganizationId == seed.Org2Id));
        await context.SaveChangesAsync();
        context.Users.RemoveRange(context.Users.Where(u => u.OrganizationId == seed.Org1Id || u.OrganizationId == seed.Org2Id));
        await context.SaveChangesAsync();
        context.Organizations.RemoveRange(context.Organizations.Where(o => o.Id == seed.Org1Id || o.Id == seed.Org2Id));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Overview_counts_organizations_users_and_companies()
    {
        // Global counts, not scoped to an isolated organization like every other DB-backed test in this
        // project - a before/after delta is racy under xUnit's default cross-class parallelization (another
        // suite's own isolated Organization can be created/cleaned up concurrently, moving the "before"
        // baseline itself between the two reads). Asserting an absolute floor after seeding avoids depending
        // on a "before" snapshot entirely while still proving GetOverviewCounts reflects real inserted rows.
        IManagementHandler handler = new ManagementHandler(Settings());

        Seed seed = await SeedTwoOrganizations();
        try
        {
            ManagementOverviewDto after = await handler.GetOverviewCounts();

            Assert.True(after.TotalOrganizations >= 2);
            Assert.True(after.TotalUsers >= 3);
            Assert.True(after.TotalCompanies >= 3);
        }
        finally
        {
            await Cleanup(seed);
        }
    }

    [Fact]
    public async Task Organizations_endpoint_returns_multiple_tenants_with_correct_counts_search_and_pagination()
    {
        Seed seed = await SeedTwoOrganizations();
        try
        {
            IManagementHandler handler = new ManagementHandler(Settings());

            // Search isolates exactly our two seeded organizations regardless of whatever else exists in the DB.
            (List<ManagementOrganizationListItemDto> allMatches, int totalCount) = await handler.GetOrganizationsPaged(new PagedRequest { Search = seed.Marker, Page = 1, PageSize = 20 });
            Assert.Equal(2, totalCount);
            Assert.Equal(2, allMatches.Count);

            ManagementOrganizationListItemDto org1 = allMatches.Single(o => o.OrganizationId == seed.Org1Id);
            Assert.Equal(2, org1.CompanyCount);
            Assert.Equal(2, org1.UserCount);

            ManagementOrganizationListItemDto org2 = allMatches.Single(o => o.OrganizationId == seed.Org2Id);
            Assert.Equal(1, org2.CompanyCount);
            Assert.Equal(1, org2.UserCount);

            // Pagination: same search, page size 1 - two pages, one item each, still summing to the same total.
            (List<ManagementOrganizationListItemDto> page1, int pagedTotalCount) = await handler.GetOrganizationsPaged(new PagedRequest { Search = seed.Marker, Page = 1, PageSize = 1 });
            Assert.Equal(2, pagedTotalCount);
            Assert.Single(page1);
        }
        finally
        {
            await Cleanup(seed);
        }
    }

    [Fact]
    public async Task Organization_detail_returns_correct_organization_specific_information()
    {
        Seed seed = await SeedTwoOrganizations();
        try
        {
            IManagementHandler handler = new ManagementHandler(Settings());

            ManagementOrganizationDetailDto detail = await handler.GetOrganizationDetail(seed.Org1Id);

            Assert.NotNull(detail);
            Assert.Equal(seed.Org1Id, detail.OrganizationId);
            Assert.Equal(2, detail.UserCount);
            Assert.Equal(2, detail.Companies.Count);
            Assert.DoesNotContain(detail.Companies, c => c.Name == "Org2-Company-A");
        }
        finally
        {
            await Cleanup(seed);
        }
    }

    [Fact]
    public async Task Organization_detail_returns_null_for_an_unknown_organization()
    {
        IManagementHandler handler = new ManagementHandler(Settings());
        Assert.Null(await handler.GetOrganizationDetail(Guid.NewGuid()));
    }

    [Fact]
    public async Task Users_endpoint_is_cross_tenant_and_supports_organization_and_active_filtering()
    {
        Seed seed = await SeedTwoOrganizations();
        try
        {
            IManagementHandler handler = new ManagementHandler(Settings());

            // Cross-tenant: no organizationId filter, search narrows to our seeded users across BOTH organizations.
            (List<ManagementUserListItemDto> crossTenant, int crossTenantTotal) = await handler.GetUsersPaged(new PagedRequest { Search = seed.Marker, Page = 1, PageSize = 20 }, organizationId: null);
            Assert.Equal(3, crossTenantTotal);
            Assert.Contains(crossTenant, u => u.OrganizationId == seed.Org1Id);
            Assert.Contains(crossTenant, u => u.OrganizationId == seed.Org2Id);

            // Organization filter narrows to exactly that tenant's users.
            (List<ManagementUserListItemDto> org1Only, int org1Total) = await handler.GetUsersPaged(new PagedRequest { Search = seed.Marker, Page = 1, PageSize = 20 }, organizationId: seed.Org1Id);
            Assert.Equal(2, org1Total);
            Assert.All(org1Only, u => Assert.Equal(seed.Org1Id, u.OrganizationId));

            // Active/inactive filter (org1User2 was seeded IsActive=false).
            (List<ManagementUserListItemDto> activeOnly, int activeTotal) = await handler.GetUsersPaged(new PagedRequest { Search = seed.Marker, IsActive = true, Page = 1, PageSize = 20 }, organizationId: null);
            Assert.Equal(2, activeTotal);
            Assert.DoesNotContain(activeOnly, u => u.UserId == seed.Org1User2Id);

            (List<ManagementUserListItemDto> inactiveOnly, int inactiveTotal) = await handler.GetUsersPaged(new PagedRequest { Search = seed.Marker, IsActive = false, Page = 1, PageSize = 20 }, organizationId: null);
            Assert.Equal(1, inactiveTotal);
            Assert.Equal(seed.Org1User2Id, inactiveOnly.Single().UserId);
        }
        finally
        {
            await Cleanup(seed);
        }
    }

    [Fact]
    public async Task Existing_tenant_scoped_CompanyHandler_stays_isolated_alongside_the_new_cross_tenant_ManagementHandler()
    {
        Seed seed = await SeedTwoOrganizations();
        try
        {
            ICompanyHandler companyHandler = new CompanyHandler(Settings(), TestClock.Source);

            (List<Company> org1Companies, int org1Total) = await companyHandler.GetPaged(seed.Org1Id, new PagedRequest { PageSize = 20 });
            Assert.Equal(2, org1Total);
            Assert.All(org1Companies, c => Assert.Equal(seed.Org1Id, c.OrganizationId));
            Assert.DoesNotContain(org1Companies, c => c.Name == "Org2-Company-A");
        }
        finally
        {
            await Cleanup(seed);
        }
    }
}
