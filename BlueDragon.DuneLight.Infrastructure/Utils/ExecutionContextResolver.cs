using System;
using System.Linq;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje prevodi izvršnu jedinicu u izvršni kontekst za cijenu, pakete, proviziju, rok otkazivanja i
/// blagajnu. Phase M1B: izvršna jedinica je SEGMENT (ili sudjelovanje na segmentu) — termin nema kontekst (može imati
/// više usluga/zaposlenika/početaka), pa "kontekst termina" više ne postoji.
///
/// Čisto preslikavanje nad VEĆ učitanim entitetima, bez upita u bazu: pozivatelji rade unutar vlastite transakcije i
/// ponekad nad segmentom koji su upravo stvorili (npr. CompleteNow stvara segment pa u istoj transakciji zarađuje
/// proviziju) — kontekst odražava točno te vrijednosti. Pozivatelji moraju učitati segment (+ Employees).
/// </summary>
public static class ExecutionContextResolver
{
    /// <summary>Segment mora pripadati terminu (programska greška inače).</summary>
    public static SegmentExecutionContext ForSegment(Appointment appointment, AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(segment);
        if (!appointment.Id.HasValue || !segment.Id.HasValue)
            throw new InvalidOperationException("Izvršni kontekst zahtijeva termin i segment s dodijeljenim Id-em.");
        if (segment.AppointmentId != appointment.Id.Value || segment.OrganizationId != appointment.OrganizationId)
            throw new InvalidOperationException("Segment ne pripada zadanom terminu.");

        return new SegmentExecutionContext(
            appointment.OrganizationId,
            appointment.Id.Value,
            segment.Id.Value,
            appointment.CompanyId,
            segment.ServiceId,
            segment.Service?.Name,
            segment.Employees.Select(e => e.EmployeeId).OrderBy(id => id).ToList(),
            SegmentPricingSource.PricingEmployeeOf(segment),
            segment.PlannedStart);
    }

    /// <summary>Kontekst ADRESIRANOG sudjelovanja: okvir (usluga, zaposlenik, početak) čita se iz SEGMENTA tog sudjelovanja.
    /// Sudjelovanje mora pripadati Bookingu, a Booking terminu (programska greška inače).</summary>
    public static ParticipationExecutionContext ForParticipation(Appointment appointment, Booking booking, BookingSegmentParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(participation);
        if (!appointment.Id.HasValue || !booking.Id.HasValue)
            throw new InvalidOperationException("Izvršni kontekst zahtijeva termin i booking s dodijeljenim Id-em.");
        if (booking.OrganizationId != appointment.OrganizationId || booking.AppointmentId != appointment.Id.Value)
            throw new InvalidOperationException("Booking ne pripada zadanom terminu.");
        if (participation.BookingId != booking.Id.Value || participation.OrganizationId != booking.OrganizationId)
            throw new InvalidOperationException("Sudjelovanje ne pripada zadanom Bookingu.");

        AppointmentSegment segment = appointment.Segments.SingleOrDefault(s => s.Id == participation.AppointmentSegmentId)
            ?? throw new InvalidOperationException("Segment sudjelovanja nije učitan na terminu (ili ne pripada terminu).");

        return new ParticipationExecutionContext(
            ForSegment(appointment, segment), booking.Id.Value, participation.Id.GetValueOrDefault(), booking.ClientId);
    }
}
