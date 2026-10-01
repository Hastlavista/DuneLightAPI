using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Outbox.Handlers;

/// <summary>Pretvara booking.cancelled.v1 u logičan Notification (vidi spec section 41). Isti zaključavanje-pa-
/// odluči obrazac i isto occurrence-svjesno rasuđivanje kao BookingNoShowNotificationHandler (vidi tamo za punu
/// napomenu) — serijalizira se s BookingService.SetStatus korekcijom (Cancelled -&gt; Confirmed, vidi spec section
/// 13) preko FOR UPDATE na istom retku SUDJELOVANJA (Phase M0), i razlikuje OVU konkretnu Cancelled pojavu (SourceVersion =
/// StatusVersion sudjelovanja u trenutku emitiranja) od bilo koje kasnije pojave na istom (grupnom, ciklirajućem)
/// sudjelovanju.</summary>
public class BookingCancelledNotificationHandler : IOutboxMessageHandler
{
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly INotificationHandler _notificationHandler;

    public BookingCancelledNotificationHandler(IBookingSegmentParticipationHandler participationHandler, INotificationHandler notificationHandler)
    {
        _participationHandler = participationHandler;
        _notificationHandler = notificationHandler;
    }

    public string Type => OutboxEventTypes.BookingCancelledV1;

    public async Task Handle(IUnitOfWork uow, Guid? organizationId, string payload, CancellationToken cancellationToken)
    {
        BookingCancelledEvent @event = JsonSerializer.Deserialize<BookingCancelledEvent>(payload, OutboxJsonOptions.Instance);
        if (@event == null)
            throw new InvalidOperationException($"Prazan/nevaljan payload za {Type}.");

        bool exists = await _notificationHandler.ExistsForSource(
            uow, @event.OrganizationId, NotificationType.BookingCancelled, NotificationSourceType.Participation, @event.ParticipationId, @event.StatusVersion);
        if (exists)
            return;

        Client client = await uow.Context.Clients.SingleOrDefaultAsync(
            c => c.Id == @event.ClientId && c.OrganizationId == @event.OrganizationId, cancellationToken);
        if (client == null)
            throw new InvalidOperationException($"Client {@event.ClientId} nije pronađen za organizaciju {@event.OrganizationId}.");

        // Phase M0: zaključava SUDJELOVANJE (ne Booking redak) — serijalizira se s korekcijom istog sudjelovanja.
        Booking booking = await _participationHandler.GetBookingWithLockedParticipation(
            uow, @event.OrganizationId, @event.ParticipationId, cancellationToken);
        if (booking == null)
            throw new InvalidOperationException($"Sudjelovanje {@event.ParticipationId} nije pronađeno za organizaciju {@event.OrganizationId}.");
        BookingSegmentParticipation participation = BookingParticipations.ById(booking, @event.ParticipationId);

        // Pending SAMO ako je booking JOŠ na TOČNO ovoj Cancelled pojavi (isti StatusVersion kao event) —
        // korigiran (Cancelled -> Confirmed) ILI odavno prošao kroz noviju pojavu, oboje daju Cancelled (vidi
        // spec section 14/37/42).
        NotificationStatus status = client.IsAnonymized ||
            participation.Status != ParticipationStatus.Cancelled || participation.StatusVersion != @event.StatusVersion
                ? NotificationStatus.Cancelled
                : NotificationStatus.Pending;

        string data = JsonSerializer.Serialize(new
        {
            appointmentId = @event.AppointmentId,
            bookingId = @event.BookingId,
            participationId = @event.ParticipationId,
            companyId = @event.CompanyId
        }, OutboxJsonOptions.Instance);

        await _notificationHandler.Add(uow, new Notification
        {
            Id = Guid.NewGuid(),
            OrganizationId = @event.OrganizationId,
            ClientId = @event.ClientId,
            Type = NotificationType.BookingCancelled,
            SourceType = NotificationSourceType.Participation,
            SourceId = @event.ParticipationId,
            SourceVersion = @event.StatusVersion,
            Status = status,
            Data = data,
            OccurredAt = @event.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
