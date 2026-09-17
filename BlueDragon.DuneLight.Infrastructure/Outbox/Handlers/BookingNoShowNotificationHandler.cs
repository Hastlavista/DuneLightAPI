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
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Outbox.Handlers;

/// <summary>Pretvara booking.no-show.v1 u logičan Notification (vidi spec section 41). Zaključava Booking redak
/// (FOR UPDATE) PRIJE donošenja Notification odluke — serijalizira ovaj handler s BookingService.SetStatus
/// administrativnom korekcijom (NoShow -&gt; Confirmed) na ISTOM retku umjesto nesigurnog read-then-decide (vidi
/// spec section 2-4/39), tako da PostgreSQL row-lock garantira JEDAN od dva ishoda bez obzira koji konkurent prvi
/// stigne. Idempotentan preko INotificationHandler.ExistsForSource po (SourceId, SourceVersion) — SourceVersion =
/// Booking.StatusVersion u trenutku emitiranja ovog eventa, ne samog Bookinga (koji na grupnim terminima
/// legitimno ciklira, vidi spec section 5-18), pa zakasnjeli event za STARIJU pojavu (nakon koje je Booking
/// otad prošao kroz noviju NoShow pojavu) NIKAD ne "oživi" staru pojavu kao Pending (vidi spec section 14/42).</summary>
public class BookingNoShowNotificationHandler : IOutboxMessageHandler
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly INotificationHandler _notificationHandler;

    public BookingNoShowNotificationHandler(IAppointmentHandler appointmentHandler, INotificationHandler notificationHandler)
    {
        _appointmentHandler = appointmentHandler;
        _notificationHandler = notificationHandler;
    }

    public string Type => OutboxEventTypes.BookingNoShowV1;

    public async Task Handle(IUnitOfWork uow, Guid? organizationId, string payload, CancellationToken cancellationToken)
    {
        BookingNoShowEvent @event = JsonSerializer.Deserialize<BookingNoShowEvent>(payload, OutboxJsonOptions.Instance);
        if (@event == null)
            throw new InvalidOperationException($"Prazan/nevaljan payload za {Type}.");

        bool exists = await _notificationHandler.ExistsForSource(
            uow, @event.OrganizationId, NotificationType.BookingNoShow, NotificationSourceType.Booking, @event.BookingId, @event.StatusVersion);
        if (exists)
            return;

        // Nepostojeći Client = processing failure (vidi spec section 43) — perzistirani event referencira redak
        // koji bi trebao postojati (ista transakcija ga je stvorila), pa je ovo znak podatkovne nekonzistentnosti.
        Client client = await uow.Context.Clients.SingleOrDefaultAsync(
            c => c.Id == @event.ClientId && c.OrganizationId == @event.OrganizationId, cancellationToken);
        if (client == null)
            throw new InvalidOperationException($"Client {@event.ClientId} nije pronađen za organizaciju {@event.OrganizationId}.");

        Booking booking = await _appointmentHandler.GetBookingForUpdate(uow, @event.OrganizationId, @event.BookingId, cancellationToken);
        if (booking == null)
            throw new InvalidOperationException($"Booking {@event.BookingId} nije pronađen za organizaciju {@event.OrganizationId}.");

        // Pending SAMO ako je booking JOŠ na TOČNO ovoj NoShow pojavi (isti StatusVersion kao event) — korigiran
        // (NoShow -> Confirmed) ILI odavno prošao kroz noviju pojavu, oboje daju Cancelled (vidi spec section
        // 14/37/42, isto ponašanje kao anonimizacija: "ne treba se dogoditi komunikacija", samo drugi razlog).
        NotificationStatus status = client.IsAnonymized ||
            booking.Status != BookingStatus.NoShow || booking.StatusVersion != @event.StatusVersion
                ? NotificationStatus.Cancelled
                : NotificationStatus.Pending;

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
            Type = NotificationType.BookingNoShow,
            SourceType = NotificationSourceType.Booking,
            SourceId = @event.BookingId,
            SourceVersion = @event.StatusVersion,
            Status = status,
            Data = data,
            OccurredAt = @event.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
