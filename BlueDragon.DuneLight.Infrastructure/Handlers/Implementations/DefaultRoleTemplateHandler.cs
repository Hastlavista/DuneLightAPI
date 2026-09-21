using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class DefaultRoleTemplateHandler : IDefaultRoleTemplateHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public DefaultRoleTemplateHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<ResolvedDefaultRoleTemplate> GetLatestActiveByKey(string key)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        DefaultRoleTemplate template = await context.DefaultRoleTemplates
            .Where(t => t.Key == key && t.IsActive)
            .OrderByDescending(t => t.Version)
            .Include(t => t.Capabilities).ThenInclude(c => c.CapabilityDefinition).ThenInclude(c => c.Grants)
            .Include(t => t.CompatibilityGrants)
            .FirstOrDefaultAsync();

        if (template == null)
            return null;

        return ToResolved(template);
    }

    public async Task<List<DefaultRoleTemplate>> GetLatestActive()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<DefaultRoleTemplate> active = await context.DefaultRoleTemplates
            .Include(t => t.Capabilities).ThenInclude(c => c.CapabilityDefinition)
            .Include(t => t.CompatibilityGrants)
            .Where(t => t.IsActive)
            .ToListAsync();

        return active
            .GroupBy(t => t.Key)
            .Select(g => g.OrderByDescending(t => t.Version).First())
            .OrderBy(t => t.Key)
            .ToList();
    }

    public async Task<DefaultRoleTemplate> GetByKey(string key, int? version)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<DefaultRoleTemplate> query = context.DefaultRoleTemplates
            .Include(t => t.Capabilities).ThenInclude(c => c.CapabilityDefinition)
            .Include(t => t.CompatibilityGrants)
            .Where(t => t.Key == key);

        if (version.HasValue)
            return await query.SingleOrDefaultAsync(t => t.Version == version.Value);

        return await query.Where(t => t.IsActive).OrderByDescending(t => t.Version).FirstOrDefaultAsync();
    }

    public async Task<List<DefaultRoleTemplate>> GetAllForDiagnostics()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.DefaultRoleTemplates
            .Include(t => t.Capabilities).ThenInclude(c => c.CapabilityDefinition)
            .Include(t => t.CompatibilityGrants)
            .OrderBy(t => t.Key).ThenBy(t => t.Version)
            .ToListAsync();
    }

    private static ResolvedDefaultRoleTemplate ToResolved(DefaultRoleTemplate template)
    {
        List<TemplateCapabilitySelection> selections = template.Capabilities
            .Select(c => new TemplateCapabilitySelection(
                c.CapabilityDefinitionId,
                c.CapabilityDefinition.Key,
                c.CapabilityDefinition.Version,
                c.CapabilityDefinition.ScopeModel,
                c.CapabilityDefinition.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role)).ToList(),
                c.SelectedScope))
            .ToList();

        List<TemplateCompatibilityGrant> compatibilityGrants = template.CompatibilityGrants
            .Select(g => new TemplateCompatibilityGrant(g.GrantKey, g.Reason))
            .ToList();

        return new ResolvedDefaultRoleTemplate(
            template.Id.GetValueOrDefault(),
            template.Key,
            template.Version,
            template.DisplayNameHr,
            selections,
            compatibilityGrants);
    }
}
