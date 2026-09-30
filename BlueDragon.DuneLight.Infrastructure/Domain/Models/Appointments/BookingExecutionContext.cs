using System;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Izvršni kontekst jednog klijenta na jednom izvođenju — <see cref="AppointmentExecutionContext"/> + Booking/Client.
/// Danas Booking → Appointment; u ciljnom modelu BookingSegmentParticipation → AppointmentSegment. Komercijalno stanje
/// (Amount, status, paket-pokriće) NIJE ovdje — ostaje na Bookingu. Stvara se isključivo kroz
/// <see cref="Utils.ExecutionContextResolver"/>.
/// </summary>
public sealed record BookingExecutionContext : AppointmentExecutionContext
{
    public BookingExecutionContext(AppointmentExecutionContext execution, Guid bookingId, Guid clientId)
        : base(execution)
    {
        BookingId = bookingId;
        ClientId = clientId;
    }

    public Guid BookingId { get; }

    public Guid ClientId { get; }
}
