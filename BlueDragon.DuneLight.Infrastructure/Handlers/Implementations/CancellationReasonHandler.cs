using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CancellationReasonHandler : ICancellationReasonHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CancellationReasonHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<CancellationReason>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationReasons.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<CancellationReason> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationReasons.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task Add(CancellationReason reason)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CancellationReasons.Add(reason);
        await context.SaveChangesAsync();
    }

    public async Task Update(CancellationReason reason)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CancellationReasons.Update(reason);
        await context.SaveChangesAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        string key = name?.Trim().ToLower() ?? string.Empty;
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationReasons.AnyAsync(r =>
            r.OrganizationId == organizationId && r.IsActive && r.Name.Trim().ToLower() == key && (excludeId == null || r.Id != excludeId));
    }
}
