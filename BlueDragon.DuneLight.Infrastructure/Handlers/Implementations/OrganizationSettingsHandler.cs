using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class OrganizationSettingsHandler : IOrganizationSettingsHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public OrganizationSettingsHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<OrganizationSettings> GetByOrganizationId(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.OrganizationSettings.SingleOrDefaultAsync(s => s.OrganizationId == organizationId);
    }

    public async Task Add(OrganizationSettings settings)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.OrganizationSettings.Add(settings);
        await context.SaveChangesAsync();
    }

    public async Task<string> GetTimeZone(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.TimeZone)
            .SingleOrDefaultAsync();
    }

    public async Task<bool> UpdateTimeZone(Guid organizationId, string timeZone)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Organization organization = await context.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId);
        if (organization == null)
            return false;

        organization.TimeZone = timeZone;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task Update(OrganizationSettings settings)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.OrganizationSettings.Update(settings);
        await context.SaveChangesAsync();
    }
}
