using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Zauzetost rasporeda koju jedan termin stvara — jedini oblik u kojem provjere preklapanja (trener, prostorija,
/// klijent) i available-slots vide postojeće termine (vidi ISchedulingOccupancyHandler). Nije EF entitet i nema
/// tablicu: projekcija nad Appointment/Booking retcima.
///
/// End = Start + Appointment.DurationMinutes, izračunato u memoriji iz iste Start vrijednosti (isti offset kao
/// učitani Appointment.StartsAt — vidi F-19 u docs/appointment-booking-characterization.md). ActiveClientIds sadrži
/// klijente čiji Booking na ovom terminu NIJE Cancelled/NoShow (klijent koji je otkazao/izostao ne zauzima raspored).
/// </summary>
public sealed record OccupancySlot(
    Guid AppointmentId,
    DateTimeOffset Start,
    DateTimeOffset End,
    Guid? EmployeeId,
    Guid? RoomId,
    IReadOnlyList<Guid> ActiveClientIds)
{
    /// <summary>Standardno pravilo preklapanja (Start &lt; end &amp;&amp; start &lt; End) — susjedni intervali (kraj
    /// jednog = početak drugog) NISU sudar.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;
}
