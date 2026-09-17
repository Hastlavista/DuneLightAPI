using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Notifications;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Notifications;

/// <summary>Read-only povijest logičkih Notification namjera (vidi Notification.cs) — nema Manage jer ništa se
/// ovdje ne konfigurira/mutira kroz API (vidi spec section 30/58).</summary>
public interface INotificationService
{
    Task<PagedResult<NotificationDto>> GetForClient(Guid organizationId, NotificationQuery query);
}
