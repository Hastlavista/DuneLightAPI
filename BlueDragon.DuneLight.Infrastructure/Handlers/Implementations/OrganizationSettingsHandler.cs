using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
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

    public async Task Update(OrganizationSettings settings)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.OrganizationSettings.Update(settings);
        await context.SaveChangesAsync();
    }
}
