using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class RoomHandler : IRoomHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public RoomHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<Room> IncludeGraph(IQueryable<Room> query)
    {
        return query.Include(r => r.Company);
    }

    public async Task<(List<Room> Items, int TotalCount)> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Room> query = IncludeGraph(context.Rooms).Where(r => r.OrganizationId == organizationId);

        if (companyId.HasValue)
            query = query.Where(r => r.CompanyId == companyId.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(r => EF.Functions.ILike(r.Name, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(r => r.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<Room> items = await query
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Name)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Room> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Rooms).SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task<List<Room>> GetByIds(Guid organizationId, List<Guid> ids)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Rooms
            .Where(r => r.OrganizationId == organizationId && r.Id.HasValue && ids.Contains(r.Id.Value))
            .ToListAsync();
    }

    public async Task Add(Room room)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Rooms.Add(room);
        await context.SaveChangesAsync();
    }

    public async Task Update(Room room)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Rooms.Update(room);
        context.Entry(room).Property(x => x.Capacity).IsModified = false;
        await context.SaveChangesAsync();
    }

    public Task<Room> GetForCapacityChange(IUnitOfWork uow, Guid organizationId, Guid id) =>
        uow.Context.Rooms.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == id);

    public async Task Delete(Room room)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Rooms.Remove(room);
        await context.SaveChangesAsync();
    }

    public async Task<bool> NameExistsAmongActive(Guid organizationId, Guid companyId, string name, Guid? excludeId)
    {
        string normalized = Normalize(name);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Rooms.AnyAsync(r =>
            r.OrganizationId == organizationId &&
            r.CompanyId == companyId &&
            r.IsActive &&
            r.Name.Trim().ToLower() == normalized &&
            (excludeId == null || r.Id != excludeId));
    }

    /// <summary>Sve poznate FK reference na prostoriju, provjerene u jednom upitu (UNION podupita) umjesto
    /// dva zasebna round-tripa — isti obrazac kao CompanyHandler.IsReferenced.</summary>
    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        // Phase M1F: prostorija grupe živi na njezinim predlošcima segmenata.
        IQueryable<int> groups = context.GroupSegmentTemplates
            .Where(t => t.Group.OrganizationId == organizationId && t.RoomId == id)
            .Select(t => 1);

        // Phase D3A: prostorija termina živi na njegovom segmentu (Restrict FK).
        IQueryable<int> segments = context.AppointmentSegments
            .Where(s => s.OrganizationId == organizationId && s.RoomId == id)
            .Select(s => 1);

        return await groups.Union(segments).AnyAsync();
    }

    private static string Normalize(string name)
    {
        return name?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
