using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

/// <summary>T1-4 — PRIVREMENI testni alat (seed), uklanja se prije go-livea.</summary>
public class DemoSeedHandler : IDemoSeedHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public DemoSeedHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<Guid?> FindActiveAdminUserId(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await (
                from assignment in context.UserGrantGroups
                join grantGroup in context.GrantGroups on (Guid?)assignment.GrantGroupId equals grantGroup.Id
                join user in context.Users on (Guid?)assignment.UserId equals user.Id
                where grantGroup.OrganizationId == organizationId
                      && grantGroup.SystemKey == SystemGrantGroups.Admin
                      && user.OrganizationId == organizationId
                      && user.IsActive
                orderby user.CreatedAt, user.Id
                select user.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid?> FindSystemAdminGrantGroupId(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Where(g => g.OrganizationId == organizationId && g.SystemKey == SystemGrantGroups.Admin)
            .OrderBy(g => g.CreatedAt).ThenBy(g => g.Id)
            .Select(g => g.Id)
            .FirstOrDefaultAsync();
    }
}
