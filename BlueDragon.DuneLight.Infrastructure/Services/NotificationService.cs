using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Notifications;
using BlueDragon.DuneLight.Core.Interfaces.Notifications;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Read-only povijest — vidi INotificationService/Notification.cs. Ništa se ovdje ne mutira.</summary>
public class NotificationService : INotificationService
{
    private readonly INotificationHandler _notificationHandler;

    public NotificationService(INotificationHandler notificationHandler)
    {
        _notificationHandler = notificationHandler;
    }

    public async Task<PagedResult<NotificationDto>> GetForClient(Guid organizationId, NotificationQuery query)
    {
        (List<Notification> items, int totalCount) = await _notificationHandler.GetPaged(organizationId, query);

        List<NotificationDto> dtos = items.Select(ToDto).ToList();
        return PagedResult<NotificationDto>.Create(dtos, totalCount, query.Page, query.PageSize);
    }

    private static NotificationDto ToDto(Notification notification)
    {
        return new NotificationDto
        {
            Id = notification.Id.GetValueOrDefault(),
            ClientId = notification.ClientId,
            ClientName = notification.Client != null ? $"{notification.Client.FirstName} {notification.Client.LastName}" : null,
            Type = notification.Type,
            SourceType = notification.SourceType,
            SourceId = notification.SourceId,
            SourceVersion = notification.SourceVersion,
            Status = notification.Status,
            Data = notification.Data,
            OccurredAt = notification.OccurredAt,
            CreatedAt = notification.CreatedAt
        };
    }
}
