using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.DTOs.Notifications;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public string ClientName { get; set; }
    public NotificationType Type { get; set; }
    public NotificationSourceType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public int SourceVersion { get; set; }
    public NotificationStatus Status { get; set; }
    public string Data { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Interna/operativna povijest (vidi spec section 30) — namjerno bez "sva povijest" upita, ClientId je
/// obavezan filtar (isti obrazac kao CommissionEntryQuery obaveznog datumskog raspona). Page/PageSize nasljeđuju
/// zajedničku PagedRequest granicu (min/max/default) umjesto vlastite neograničene definicije — isti obrazac
/// kao ostali paginirani upiti u ovoj bazi (vidi spec section 32).</summary>
public class NotificationQuery : PagedRequest
{
    public Guid ClientId { get; set; }
}
