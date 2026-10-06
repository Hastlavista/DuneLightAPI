#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0023 — inicijalizacija organizacije pri registraciji (AuthService.Register), bez ikakvog seeda u bazi: u istoj
/// transakciji nastaju Organization, prvi User, JEDNA Admin GrantGroup (system_key 'admin') sa SVIM grantovima iz
/// Grants.Catalog i dodjela prvog Usera toj grupi, plus zadani RosterTypeovi. Bez Owner bypassa ovo je jedini način na
/// koji osnivač organizacije dobiva ikakav pristup.
/// </summary>
public class RegistrationBootstrapTests
{
    private static AuthService NewAuthService(SchedulingWorld w) => new(
        w.Resolve<IAuthHandler>(),
        w.Resolve<IRosterTypeHandler>(),
        w.Resolve<IGrantGroupHandler>(),
        new JwtService(MultiSegmentHttpContractTests.Jwt),
        MultiSegmentHttpContractTests.Jwt,
        w.Resolve<IUnitOfWorkFactory>());

    [Fact]
    public async Task Register_CreatesExactlyOneSystemAdminGroup_WithTheWholeGrantCatalog_AndAssignsTheFounder()
    {
        // The world only provides the DI container; registration creates its own organization.
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Register_CreatesExactlyOneSystemAdminGroup_WithTheWholeGrantCatalog_AndAssignsTheFounder));
        Guid suffix = Guid.NewGuid();

        AuthResponse response = await NewAuthService(w).Register(new RegisterRequest
        {
            OrganizationName = $"Registration bootstrap {suffix:N}",
            Email = $"founder-{suffix:N}@registration.test",
            Password = "Founder-Password-1"
        });
        Guid organizationId = response.OrganizationId.GetValueOrDefault();

        try
        {
            await using DatabaseContext db = w.NewDb();

            GrantGroup admin = Assert.Single(await db.GrantGroups.Include(g => g.Grants)
                .Where(g => g.OrganizationId == organizationId).ToListAsync());
            Assert.Equal(SystemGrantGroups.Admin, admin.SystemKey);
            Assert.Equal(SystemGrantGroups.AdminDisplayName, admin.Name);
            Assert.Equal(Grants.Catalog.Select(g => g.Key).ToHashSet(), admin.Grants.Select(g => g.GrantKey).ToHashSet());

            Assert.True(await db.UserGrantGroups.AnyAsync(u => u.UserId == response.UserId && u.GrantGroupId == admin.Id));

            HashSet<string> effective = await w.Resolve<IGrantGroupHandler>().ResolveEffective(organizationId, response.UserId.GetValueOrDefault());
            Assert.Contains(Grants.PermissionsManage, effective);

            // Organization initialization also creates the default roster types (application code, not a migration seed).
            Assert.True(await db.RosterTypes.AnyAsync(t => t.OrganizationId == organizationId));
        }
        finally
        {
            await SchedulingWorld.DeleteOrganization(organizationId);
        }
    }

    [Fact]
    public async Task SystemAdminGroup_IsAnOrdinaryOrganizationGroup_ItCanBeRenamedAndItsGrantsChanged()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SystemAdminGroup_IsAnOrdinaryOrganizationGroup_ItCanBeRenamedAndItsGrantsChanged));
        IGrantGroupHandler handler = w.Resolve<IGrantGroupHandler>();

        Guid adminId;
        await using (IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin())
        {
            adminId = await handler.CreateSystemAdminGroup(uow, w.OrganizationId);
            await uow.CommitAsync();
        }

        GrantGroup admin = await handler.GetById(w.OrganizationId, adminId);
        admin.Name = "Vlasnici";
        await handler.Update(admin, new List<string> { Grants.PermissionsManage, Grants.PermissionsView });

        GrantGroup reloaded = await handler.GetById(w.OrganizationId, adminId);
        Assert.Equal("Vlasnici", reloaded.Name);
        Assert.Equal(SystemGrantGroups.Admin, reloaded.SystemKey);
        Assert.Equal(new HashSet<string> { Grants.PermissionsManage, Grants.PermissionsView }, reloaded.Grants.Select(g => g.GrantKey).ToHashSet());
    }
}
