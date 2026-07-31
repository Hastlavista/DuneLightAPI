using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GroupHandler : IGroupHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public GroupHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<Group> IncludeGraph(IQueryable<Group> query)
    {
        return query
            .Include(g => g.Service)
            .Include(g => g.Location)
            .Include(g => g.DefaultTrainer)
            .Include(g => g.Slots)
            .Include(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.Client);
    }

    public async Task Add(Group group)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Groups.Add(group);
        await context.SaveChangesAsync();
    }

    public async Task<Group> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Groups)
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
    }

    public async Task<Group> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Groups
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
    }

    public async Task<List<Group>> GetAll(Guid organizationId, bool? isActive)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Group> query = IncludeGraph(context.Groups).Where(g => g.OrganizationId == organizationId);

        if (isActive.HasValue)
            query = query.Where(g => g.IsActive == isActive.Value);

        return await query.OrderBy(g => g.Name).ToListAsync();
    }

    public async Task UpdateScalar(Group group)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Groups.Update(group);
        await context.SaveChangesAsync();
    }

    public async Task UpdateScalar(IUnitOfWork uow, Group group)
    {
        uow.Context.Groups.Update(group);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<GroupSlot> GetSlotById(Guid organizationId, Guid groupId, Guid slotId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupSlots.SingleOrDefaultAsync(s =>
            s.GroupId == groupId && s.Id == slotId && s.Group.OrganizationId == organizationId);
    }

    public async Task<int> CountActiveSlots(Guid groupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupSlots.CountAsync(s => s.GroupId == groupId && s.IsActive);
    }

    public async Task AddSlot(GroupSlot slot)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GroupSlots.Add(slot);
        await context.SaveChangesAsync();
    }

    public async Task UpdateSlot(GroupSlot slot)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GroupSlots.Update(slot);
        await context.SaveChangesAsync();
    }

    public async Task<GroupMember> GetActiveMember(Guid organizationId, Guid groupId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupMembers
            .SingleOrDefaultAsync(m => m.GroupId == groupId && m.ClientId == clientId && m.IsActive && m.Group.OrganizationId == organizationId);
    }

    public async Task<GroupMember> GetMemberById(Guid organizationId, Guid groupId, Guid memberId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupMembers.SingleOrDefaultAsync(m =>
            m.GroupId == groupId && m.Id == memberId && m.Group.OrganizationId == organizationId);
    }

    public async Task<int> CountActiveMembers(Guid groupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupMembers.CountAsync(m => m.GroupId == groupId && m.IsActive);
    }

    public async Task AddMember(GroupMember member)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GroupMembers.Add(member);
        await context.SaveChangesAsync();
    }

    public async Task AddMember(IUnitOfWork uow, GroupMember member)
    {
        uow.Context.GroupMembers.Add(member);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateMember(GroupMember member)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GroupMembers.Update(member);
        await context.SaveChangesAsync();
    }

    public async Task UpdateMember(IUnitOfWork uow, GroupMember member)
    {
        uow.Context.GroupMembers.Update(member);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<List<GroupMember>> GetMembershipsByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GroupMembers
            .Include(m => m.Group)
            .Where(m => m.ClientId == clientId && m.Group.OrganizationId == organizationId)
            .OrderByDescending(m => m.JoinedAt)
            .ToListAsync();
    }

    public async Task<HashSet<(Guid GroupSlotId, DateTimeOffset StartsAt)>> GetExistingSlotOccurrences(
        List<Guid> groupSlotIds, DateTimeOffset from, DateTimeOffset to)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<(Guid, DateTimeOffset)> rows = await context.Appointments
            .Where(a => a.GroupSlotId != null && groupSlotIds.Contains(a.GroupSlotId.Value) &&
                a.StartsAt >= from && a.StartsAt <= to)
            .Select(a => new ValueTuple<Guid, DateTimeOffset>(a.GroupSlotId.Value, a.StartsAt))
            .ToListAsync();

        return rows.ToHashSet();
    }

    public async Task AddAppointments(List<Appointment> appointments)
    {
        if (appointments.Count == 0)
            return;

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.AddRange(appointments);
        await context.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetAppointmentsForGroup(Guid organizationId, Guid groupId, DateTimeOffset from, DateTimeOffset to)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Include(a => a.Service).ThenInclude(s => s.ServiceCategory)
            .Include(a => a.Employee)
            .Include(a => a.Location)
            .Include(a => a.Attendances)
            .Where(a => a.OrganizationId == organizationId && a.GroupId == groupId &&
                a.StartsAt >= from && a.StartsAt <= to)
            .OrderBy(a => a.StartsAt)
            .ToListAsync();
    }
}
