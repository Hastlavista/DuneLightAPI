using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Notifications;
using BlueDragon.DuneLight.Core.Interfaces.Notifications;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Notifications;

/// <summary>Interna/operativna povijest logičkih Notification namjera po klijentu (vidi INotificationService) —
/// nema provider/isporuku, ovo NIJE "poslane obavijesti" pregled (vidi spec section 30).</summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [RequireGrant(Grants.NotificationsView)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetForClient([FromQuery] NotificationQuery query)
    {
        return Ok(await _notificationService.GetForClient(this.CurrentOrganizationId(), query));
    }
}
