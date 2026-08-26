using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class RosterTypeHandler : IRosterTypeHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public RosterTypeHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<RosterType> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<RosterType> query = context.RosterTypes.Where(t => t.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(t => t.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<RosterType> items = await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<RosterType> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterTypes.SingleOrDefaultAsync(t => t.OrganizationId == organizationId && t.Id == id);
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterTypes.AnyAsync(t =>
            t.OrganizationId == organizationId &&
            t.IsActive &&
            t.Name == name &&
            (excludeId == null || t.Id != excludeId));
    }

    public async Task Add(RosterType rosterType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterTypes.Add(rosterType);
        await context.SaveChangesAsync();
    }

    public async Task Update(RosterType rosterType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterTypes.Update(rosterType);
        await context.SaveChangesAsync();
    }

    public async Task Delete(RosterType rosterType)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.RosterTypes.Remove(rosterType);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.RosterEntries.AnyAsync(e => e.OrganizationId == organizationId && e.RosterTypeId == id);
    }

    public async Task SeedDefaultTypes(IUnitOfWork uow, Guid organizationId)
    {
        List<RosterType> types = DefaultRosterTypes.All.Select(definition => new RosterType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = definition.Name,
            ColorHex = definition.ColorHex,
            CountsAsWork = definition.CountsAsWork,
            IsAbsence = definition.IsAbsence,
            RequiresTime = definition.RequiresTime,
            DeductsFromLeaveFund = definition.DeductsFromLeaveFund,
            SortOrder = definition.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();

        uow.Context.RosterTypes.AddRange(types);
        await uow.Context.SaveChangesAsync();
    }
}
