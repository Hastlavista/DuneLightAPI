using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ClientTagHandler : IClientTagHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientTagHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<ClientTag> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<ClientTag> query = context.ClientTags.Where(t => t.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(t => t.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<ClientTag> items = await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<ClientTag> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientTags.SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.Id == id);
    }

    public async Task<List<ClientTag>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientTags
            .Where(t => t.OrganizationId == organizationId && t.Id.HasValue && ids.Contains(t.Id.Value))
            .ToListAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientTags.AnyAsync(t =>
            t.OrganizationId == organizationId &&
            t.IsActive &&
            t.Name == name &&
            (excludeId == null || t.Id != excludeId));
    }

    public async Task Add(ClientTag tag)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientTags.Add(tag);
        await context.SaveChangesAsync();
    }

    public async Task Update(ClientTag tag)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientTags.Update(tag);
        await context.SaveChangesAsync();
    }

    public async Task Delete(ClientTag tag)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientTags.Remove(tag);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientTagAssignments.AnyAsync(a => a.TagId == id);
    }
}
