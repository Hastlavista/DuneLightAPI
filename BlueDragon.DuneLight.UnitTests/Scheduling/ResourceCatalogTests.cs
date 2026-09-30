#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Resource catalog (Phase C foundation): a generic, Company-scoped finite reusable capacity. Same conventions as Room:
/// Organization-scoped reads/writes, immutable Company, normalized active-name uniqueness per Company, active lifecycle.
/// Scheduling does not use Resources yet.
/// </summary>
public class ResourceCatalogTests
{
    private static ResourceCreateRequest CreateRequest(SchedulingWorld w, string name, int capacity = 3, Company company = null) => new()
    {
        CompanyId = (company ?? w.Company).Id.Value, Name = name, Capacity = capacity
    };

    private static ResourceUpdateRequest UpdateRequest(ResourceDto resource, int? capacity = null, string name = null) => new()
    {
        Name = name ?? resource.Name, Capacity = capacity ?? resource.Capacity, Note = resource.Note, SortOrder = resource.SortOrder
    };

    private static PagedRequest All(bool? isActive = null) => new() { Page = 1, PageSize = 50, IsActive = isActive };

    #region CRUD

    [Fact]
    public async Task Create_Get_List_ReturnTheResource()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_Get_List_ReturnTheResource));
        IResourceService resources = w.Resolve<IResourceService>();

        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, new ResourceCreateRequest
        {
            CompanyId = w.Company.Id.Value, Name = "  Reformer  ", Capacity = 6, Note = "Pilates", SortOrder = 2
        });

        Assert.Equal("Reformer", created.Name);
        Assert.Equal(6, created.Capacity);
        Assert.Equal(w.Company.Id.Value, created.CompanyId);
        Assert.Equal(w.Company.Name, created.CompanyName);
        Assert.True(created.IsActive);
        Assert.Equal(w.ActorUserId, created.CreatedBy);
        Assert.Null(created.UpdatedAt);

        ResourceDto read = await resources.GetById(w.OrganizationId, created.Id);
        Assert.Equal(created.Id, read.Id);
        Assert.Equal(6, read.Capacity);
        Assert.Equal("Pilates", read.Note);

        PagedResult<ResourceDto> page = await resources.GetPaged(w.OrganizationId, w.Company.Id.Value, All());
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);

        await using DatabaseContext db = w.NewDb();
        Resource row = await db.Resources.SingleAsync(r => r.Id == created.Id);
        Assert.Equal(w.OrganizationId, row.OrganizationId);
    }

    [Fact]
    public async Task List_FiltersByCompanyActiveStateAndSearch()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(List_FiltersByCompanyActiveStateAndSearch));
        IResourceService resources = w.Resolve<IResourceService>();
        Company second = await w.AddCompany("Second");
        ResourceDto bike = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));
        ResourceDto table = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Massage table"));
        ResourceDto sauna = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Sauna place", company: second));
        await resources.SetActive(w.OrganizationId, w.ActorUserId, table.Id, false);

        Assert.Equal(3, (await resources.GetPaged(w.OrganizationId, null, All())).TotalCount);
        Assert.Equal(new[] { sauna.Id }, (await resources.GetPaged(w.OrganizationId, second.Id.Value, All())).Items.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { bike.Id }, (await resources.GetPaged(w.OrganizationId, w.Company.Id.Value, All(isActive: true))).Items.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { table.Id }, (await resources.GetPaged(w.OrganizationId, null, All(isActive: false))).Items.Select(r => r.Id).ToArray());
        Assert.Equal(new[] { table.Id }, (await resources.GetPaged(w.OrganizationId, null, new PagedRequest { Page = 1, PageSize = 50, Search = "massage" })).Items.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Update_ChangesNameCapacityNoteAndOrder_ButNeverTheCompany()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_ChangesNameCapacityNoteAndOrder_ButNeverTheCompany));
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", 10));

        ResourceDto updated = await resources.Update(w.OrganizationId, w.ActorUserId, created.Id, new ResourceUpdateRequest
        {
            Name = "Spin bike", Capacity = 12, Note = "Row 2", SortOrder = 5
        });

        Assert.Equal("Spin bike", updated.Name);
        Assert.Equal(12, updated.Capacity);
        Assert.Equal("Row 2", updated.Note);
        Assert.Equal(5, updated.SortOrder);
        Assert.Equal(created.CompanyId, updated.CompanyId);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(w.ActorUserId, updated.UpdatedBy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task NonPositiveCapacity_IsRejected_OnCreateAndUpdate(int capacity)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(NonPositiveCapacity_IsRejected_OnCreateAndUpdate)}-{capacity}");
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto resource = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", 1));

        await SchedulingAssert.Validation(() => resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Other", capacity)));
        await SchedulingAssert.Validation(() => resources.Update(w.OrganizationId, w.ActorUserId, resource.Id, UpdateRequest(resource, capacity)));

        Assert.Equal(1, (await resources.GetById(w.OrganizationId, resource.Id)).Capacity);
        Assert.Single((await resources.GetPaged(w.OrganizationId, null, All())).Items);
    }

    [Fact]
    public async Task Database_EnforcesCapacityCheck_AndActiveNameUniqueness()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_EnforcesCapacityCheck_AndActiveNameUniqueness));
        ResourceDto existing = await w.Resolve<IResourceService>().Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));

        Resource Row(string name, int capacity, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, CompanyId = w.Company.Id.Value, Name = name,
            Capacity = capacity, IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow
        };

        await using (DatabaseContext db = w.NewDb())
        {
            db.Resources.Add(Row("Zero", 0));
            DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ck_resources_capacity_positive", ex.InnerException?.Message);
        }

        await using (DatabaseContext db = w.NewDb())
        {
            db.Resources.Add(Row(" BIKE ", 2));
            DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ux_resources_org_company_name_active", ex.InnerException?.Message);
        }

        await using (DatabaseContext db = w.NewDb())
        {
            db.Resources.Add(Row("Bike", 2, isActive: false)); // inactive rows are outside the partial unique index
            await db.SaveChangesAsync();
        }

        Assert.True((await w.Resolve<IResourceService>().GetById(w.OrganizationId, existing.Id)).IsActive);
    }

    [Fact]
    public async Task Delete_RemovesTheResource()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_RemovesTheResource));
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));

        await resources.Delete(w.OrganizationId, created.Id);

        await SchedulingAssert.NotFound(() => resources.GetById(w.OrganizationId, created.Id));
    }

    [Fact]
    public async Task Company_WithAResource_CannotBeHardDeleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Company_WithAResource_CannotBeHardDeleted));
        Company bare = await w.AddCompany("Bare", withWorkingHours: false);
        await w.Resolve<IResourceService>().Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", company: bare));

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Resolve<ICompanyService>().Delete(w.OrganizationId, bare.Id.Value));
    }

    #endregion

    #region Active lifecycle and name uniqueness

    [Fact]
    public async Task Deactivate_And_Reactivate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Deactivate_And_Reactivate));
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));

        ResourceDto inactive = await resources.SetActive(w.OrganizationId, w.ActorUserId, created.Id, false);
        Assert.False(inactive.IsActive);
        Assert.False((await resources.GetById(w.OrganizationId, created.Id)).IsActive);
        Assert.False((await resources.SetActive(w.OrganizationId, w.ActorUserId, created.Id, false)).IsActive); // idempotent

        ResourceDto active = await resources.SetActive(w.OrganizationId, w.ActorUserId, created.Id, true);
        Assert.True(active.IsActive);
    }

    [Fact]
    public async Task InactiveCompany_BlocksCreateAndReactivation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(InactiveCompany_BlocksCreateAndReactivation));
        IResourceService resources = w.Resolve<IResourceService>();
        Company second = await w.AddCompany("Second");
        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", company: second));
        await resources.SetActive(w.OrganizationId, w.ActorUserId, created.Id, false);
        await w.SetCompanyActive(second, false);

        await SchedulingAssert.Validation(() => resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Table", company: second)));
        await SchedulingAssert.Validation(() => resources.SetActive(w.OrganizationId, w.ActorUserId, created.Id, true));
    }

    [Fact]
    public async Task DuplicateActiveName_InTheSameCompany_IsRejected_CaseAndWhitespaceInsensitive()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DuplicateActiveName_InTheSameCompany_IsRejected_CaseAndWhitespaceInsensitive));
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto bike = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));
        ResourceDto table = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Table"));

        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateName,
            () => resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "  bIKE ")));
        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateName,
            () => resources.Update(w.OrganizationId, w.ActorUserId, table.Id, UpdateRequest(table, name: "BIKE")));

        // Renaming a resource to its own name (different case) is not a duplicate of itself.
        ResourceDto renamed = await resources.Update(w.OrganizationId, w.ActorUserId, bike.Id, UpdateRequest(bike, name: "bike"));
        Assert.Equal("bike", renamed.Name);
    }

    [Fact]
    public async Task SameName_IsAllowedAfterDeactivation_ButBlocksReactivationOfTheOldOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameName_IsAllowedAfterDeactivation_ButBlocksReactivationOfTheOldOne));
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto old = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));
        await resources.SetActive(w.OrganizationId, w.ActorUserId, old.Id, false);

        ResourceDto replacement = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", 4));

        Assert.True(replacement.IsActive);
        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateName,
            () => resources.SetActive(w.OrganizationId, w.ActorUserId, old.Id, true));
    }

    [Fact]
    public async Task SameName_InAnotherCompany_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameName_InAnotherCompany_IsAllowed));
        IResourceService resources = w.Resolve<IResourceService>();
        Company second = await w.AddCompany("Second");
        await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));

        ResourceDto other = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", company: second));

        Assert.Equal(second.Id.Value, other.CompanyId);
    }

    #endregion

    #region Tenant / Company isolation

    [Fact]
    public async Task UnknownCompany_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(UnknownCompany_IsRejected));
        IResourceService resources = w.Resolve<IResourceService>();

        await SchedulingAssert.NotFound(() => resources.Create(w.OrganizationId, w.ActorUserId, new ResourceCreateRequest
        {
            CompanyId = Guid.NewGuid(), Name = "Bike", Capacity = 1
        }));
    }

    [Fact]
    public async Task CompanyOfAnotherOrganization_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompanyOfAnotherOrganization_IsRejected));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(CompanyOfAnotherOrganization_IsRejected)}-other");
        IResourceService resources = w.Resolve<IResourceService>();

        await SchedulingAssert.NotFound(() => resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(other, "Bike")));

        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.Resources.AnyAsync(r => r.CompanyId == other.Company.Id || r.OrganizationId == w.OrganizationId));
    }

    [Fact]
    public async Task AnotherOrganization_CannotReadListUpdateToggleOrDelete()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnotherOrganization_CannotReadListUpdateToggleOrDelete));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(AnotherOrganization_CannotReadListUpdateToggleOrDelete)}-other");
        IResourceService resources = w.Resolve<IResourceService>();
        ResourceDto created = await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike", 5));

        await SchedulingAssert.NotFound(() => resources.GetById(other.OrganizationId, created.Id));
        await SchedulingAssert.NotFound(() => resources.Update(other.OrganizationId, other.ActorUserId, created.Id, UpdateRequest(created, 9)));
        await SchedulingAssert.NotFound(() => resources.SetActive(other.OrganizationId, other.ActorUserId, created.Id, false));
        await SchedulingAssert.NotFound(() => resources.Delete(other.OrganizationId, created.Id));
        Assert.Empty((await resources.GetPaged(other.OrganizationId, null, All())).Items);
        Assert.Empty((await resources.GetPaged(other.OrganizationId, w.Company.Id.Value, All())).Items);

        ResourceDto unchanged = await resources.GetById(w.OrganizationId, created.Id);
        Assert.Equal(5, unchanged.Capacity);
        Assert.True(unchanged.IsActive);
    }

    [Fact]
    public async Task DuplicateNameCheck_IsScopedToTheOrganization()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DuplicateNameCheck_IsScopedToTheOrganization));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(DuplicateNameCheck_IsScopedToTheOrganization)}-other");
        IResourceService resources = w.Resolve<IResourceService>();
        await resources.Create(w.OrganizationId, w.ActorUserId, CreateRequest(w, "Bike"));

        ResourceDto foreign = await resources.Create(other.OrganizationId, other.ActorUserId, CreateRequest(other, "Bike"));

        Assert.Equal(other.Company.Id.Value, foreign.CompanyId);
    }

    #endregion
}
