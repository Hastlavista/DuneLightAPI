using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CapabilityDefinitionHandler : ICapabilityDefinitionHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CapabilityDefinitionHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<CapabilityDefinition>> GetLatestActive()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<CapabilityDefinition> active = await context.CapabilityDefinitions
            .Include(c => c.Grants)
            .Where(c => c.IsActive)
            .ToListAsync();

        return active
            .GroupBy(c => c.Key)
            .Select(g => g.OrderByDescending(c => c.Version).First())
            .OrderBy(c => c.CategoryKey).ThenBy(c => c.Key)
            .ToList();
    }

    public async Task<CapabilityDefinition> GetByKey(string key, int? version)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<CapabilityDefinition> query = context.CapabilityDefinitions
            .Include(c => c.Grants)
            .Where(c => c.Key == key);

        if (version.HasValue)
            return await query.SingleOrDefaultAsync(c => c.Version == version.Value);

        return await query.Where(c => c.IsActive).OrderByDescending(c => c.Version).FirstOrDefaultAsync();
    }

    public async Task<List<CapabilityDefinition>> GetAllForDiagnostics()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CapabilityDefinitions
            .Include(c => c.Grants)
            .OrderBy(c => c.Key).ThenBy(c => c.Version)
            .ToListAsync();
    }
}
