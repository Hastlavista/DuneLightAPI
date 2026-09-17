using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Outbox.Handlers;

/// <summary>Pretvara waitlist.promoted.v1 u logičan Notification (vidi spec section 41). SourceVersion je
/// konstantno 0 — promocija je terminalna (vidi WaitlistEntryStatus), nema ciklirajućeg životnog vijeka kao
/// grupni Booking status, pa nema potrebe za occurrence-svjesnim rasuđivanjem (vidi spec section 16, Notification.cs).</summary>
public class WaitlistPromotedNotificationHandler : IOutboxMessageHandler
{
    private const int TerminalSourceVersion = 0;

    private readonly INotificationHandler _notificationHandler;

    public WaitlistPromotedNotificationHandler(INotificationHandler notificationHandler)
    {
        _notificationHandler = notificationHandler;
    }

    public string Type => OutboxEventTypes.WaitlistPromotedV1;

    public async Task Handle(IUnitOfWork uow, Guid? organizationId, string payload, CancellationToken cancellationToken)
    {
        WaitlistPromotedEvent @event = JsonSerializer.Deserialize<WaitlistPromotedEvent>(payload, OutboxJsonOptions.Instance);
        if (@event == null)
            throw new InvalidOperationException($"Prazan/nevaljan payload za {Type}.");

        bool exists = await _notificationHandler.ExistsForSource(
            uow, @event.OrganizationId, NotificationType.WaitlistPromoted, NotificationSourceType.WaitlistEntry,
            @event.WaitlistEntryId, TerminalSourceVersion);
        if (exists)
            return;

        Client client = await uow.Context.Clients.SingleOrDefaultAsync(
            c => c.Id == @event.ClientId && c.OrganizationId == @event.OrganizationId, cancellationToken);
        if (client == null)
            throw new InvalidOperationException($"Client {@event.ClientId} nije pronađen za organizaciju {@event.OrganizationId}.");

        NotificationStatus status = client.IsAnonymized ? NotificationStatus.Cancelled : NotificationStatus.Pending;

        string data = JsonSerializer.Serialize(new
        {
            appointmentId = @event.AppointmentId,
            bookingId = @event.BookingId,
            companyId = @event.CompanyId
        }, OutboxJsonOptions.Instance);

        await _notificationHandler.Add(uow, new Notification
        {
            Id = Guid.NewGuid(),
            OrganizationId = @event.OrganizationId,
            ClientId = @event.ClientId,
            Type = NotificationType.WaitlistPromoted,
            SourceType = NotificationSourceType.WaitlistEntry,
            SourceId = @event.WaitlistEntryId,
            SourceVersion = TerminalSourceVersion,
            Status = status,
            Data = data,
            OccurredAt = @event.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
