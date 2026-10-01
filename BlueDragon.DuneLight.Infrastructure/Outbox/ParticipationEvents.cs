using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Outbox;

/// <summary>
/// Phase M0 — JEDINO mjesto koje piše booking.cancelled.v1 / booking.no-show.v1 pojave (prije prepisano u BookingService,
/// AppointmentService i GroupService). Pojava pripada SUDJELOVANJU: identitet (i Outbox idempotency-key) je
/// (ParticipationId, StatusVersion NAKON prijelaza) — Booking nema vlastitu verziju, pa Booking-wide naredba daje po
/// jednu pojavu za svako sudjelovanje koje je stvarno prešlo. BookingId/AppointmentId/ClientId su kontekst.
///
/// Phase M1A.1 — PRAVILO ZA KASNIJE (multi-segment, nije implementirano): interni Outbox događaji ostaju po sudjelovanju
/// (identitet izvršne pojave), ali jedna Booking-wide ili Appointment-wide poslovna naredba treba proizvesti NAJVIŠE JEDNU
/// klijentu namijenjenu namjeru obavijesti po Bookingu/klijentu (ne identične poruke po sudjelovanju). Grupiranje se radi
/// na razini obrade obavijesti (npr. po naredbi/Bookingu), ne promjenom identiteta ovih događaja. Danas (jedno sudjelovanje
/// po Bookingu u produkciji) je to isto.
/// </summary>
public static class ParticipationEvents
{
    public static Task WriteCancelled(
        IOutboxWriter outboxWriter, IUnitOfWork uow, Guid organizationId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation)
    {
        Guid participationId = participation.Id.GetValueOrDefault();
        return outboxWriter.Add(
            uow, organizationId, OutboxEventTypes.BookingCancelledV1,
            new BookingCancelledEvent
            {
                OrganizationId = organizationId,
                BookingId = booking.Id.GetValueOrDefault(),
                ParticipationId = participationId,
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = booking.ClientId,
                CompanyId = appointment.CompanyId,
                StatusVersion = participation.StatusVersion,
                OccurredAt = DateTimeOffset.UtcNow
            },
            DateTimeOffset.UtcNow,
            idempotencyKey: $"booking-cancelled:{participationId}:{participation.StatusVersion}");
    }

    public static Task WriteNoShow(
        IOutboxWriter outboxWriter, IUnitOfWork uow, Guid organizationId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation)
    {
        Guid participationId = participation.Id.GetValueOrDefault();
        return outboxWriter.Add(
            uow, organizationId, OutboxEventTypes.BookingNoShowV1,
            new BookingNoShowEvent
            {
                OrganizationId = organizationId,
                BookingId = booking.Id.GetValueOrDefault(),
                ParticipationId = participationId,
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = booking.ClientId,
                CompanyId = appointment.CompanyId,
                StatusVersion = participation.StatusVersion,
                OccurredAt = DateTimeOffset.UtcNow
            },
            DateTimeOffset.UtcNow,
            idempotencyKey: $"booking-noshow:{participationId}:{participation.StatusVersion}");
    }
}
