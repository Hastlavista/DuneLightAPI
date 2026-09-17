using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Notifications;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class NotificationHandler : INotificationHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public NotificationHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<bool> ExistsForSource(
        IUnitOfWork uow, Guid organizationId, NotificationType type, NotificationSourceType sourceType, Guid sourceId, int sourceVersion)
    {
        return await uow.Context.Notifications.AnyAsync(n =>
            n.OrganizationId == organizationId && n.Type == type && n.SourceType == sourceType &&
            n.SourceId == sourceId && n.SourceVersion == sourceVersion);
    }

    public async Task Add(IUnitOfWork uow, Notification notification)
    {
        uow.Context.Notifications.Add(notification);
        await uow.Context.SaveChangesAsync();
    }

    public async Task CancelIfPending(
        IUnitOfWork uow, Guid organizationId, NotificationType type, NotificationSourceType sourceType, Guid sourceId, int sourceVersion)
    {
        Notification notification = await uow.Context.Notifications.SingleOrDefaultAsync(n =>
            n.OrganizationId == organizationId && n.Type == type && n.SourceType == sourceType &&
            n.SourceId == sourceId && n.SourceVersion == sourceVersion);

        if (notification == null || notification.Status != NotificationStatus.Pending)
            return;

        notification.Status = NotificationStatus.Cancelled;
        await uow.Context.SaveChangesAsync();
    }

    public async Task<(List<Notification> Items, int TotalCount)> GetPaged(Guid organizationId, NotificationQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Notification> filtered = context.Notifications
            .Where(n => n.OrganizationId == organizationId && n.ClientId == query.ClientId);

        int totalCount = await filtered.CountAsync();

        List<Notification> items = await filtered
            .Include(n => n.Client)
            .OrderByDescending(n => n.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }
}
