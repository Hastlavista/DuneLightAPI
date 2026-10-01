using System;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Phase M1B — izvršni kontekst jednog klijenta na jednom izvođenju: <see cref="SegmentExecutionContext"/> SEGMENTA
/// sudjelovanja + Booking/Client/Participation. Komercijalno stanje nije ovdje (nosi ga sudjelovanje). Stvara se
/// isključivo kroz <see cref="Utils.ExecutionContextResolver.ForParticipation"/>.
/// </summary>
public sealed record ParticipationExecutionContext : SegmentExecutionContext
{
    public ParticipationExecutionContext(SegmentExecutionContext execution, Guid bookingId, Guid participationId, Guid clientId)
        : base(execution)
    {
        BookingId = bookingId;
        ParticipationId = participationId;
        ClientId = clientId;
    }

    public Guid BookingId { get; }

    public Guid ParticipationId { get; }

    public Guid ClientId { get; }
}
