using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
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
        await context.SaveChangesAsync();
    }

    public async Task Delete(Room room)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Rooms.Remove(room);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        bool referencedByAppointment = await context.Appointments.AnyAsync(a => a.OrganizationId == organizationId && a.RoomId == id);
        if (referencedByAppointment)
            return true;

        return await context.Groups.AnyAsync(g => g.OrganizationId == organizationId && g.DefaultRoomId == id);
    }
}
