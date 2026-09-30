using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje prevodi izvršni okvir termina (od Phase D3A: njegov jedini AppointmentSegment) u izvršni
/// kontekst za cijenu, pakete, proviziju, rok otkazivanja i blagajnu — komercijalna logika čita
/// <see cref="AppointmentExecutionContext"/>/<see cref="BookingExecutionContext"/>, ne Appointment polja izravno.
///
/// Čisto preslikavanje nad VEĆ učitanim entitetima, bez upita u bazu: pozivatelji rade unutar vlastite transakcije i
/// ponekad nad Appointmentom koji su upravo izmijenili (npr. CompleteExisting mijenja okvir segmenta pa tek onda
/// zarađuje proviziju) — kontekst mora odražavati točno te vrijednosti, kao i prije. Pozivatelji moraju učitati
/// Segments (+ Employees). Kad Booking prijeđe na BookingSegmentParticipation, ovdje se mijenja izvor, ne pozivatelji.
/// </summary>
public static class ExecutionContextResolver
{
    public static AppointmentExecutionContext ForAppointment(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (!appointment.Id.HasValue)
            throw new InvalidOperationException("Izvršni kontekst zahtijeva termin s dodijeljenim Id-em.");

        // Phase D3A: okvir se čita iz jedinog autoritativnog segmenta (ServiceName iz segment.Service ako je učitan —
        // ista semantika kao prije s appointment.Service).
        AppointmentSegment segment = AppointmentSegments.GetSingleExecutionSegment(appointment);
        return new AppointmentExecutionContext(
            appointment.OrganizationId,
            appointment.Id.Value,
            appointment.CompanyId,
            segment.ServiceId,
            segment.Service?.Name,
            AppointmentSegments.GetSingleEmployeeId(segment),
            segment.PlannedStart);
    }

    /// <summary>Booking mora pripadati TOM terminu i TOJ organizaciji — neusklađen par je programska greška pozivatelja
    /// (nikad valjan poslovni slučaj), pa se odbija umjesto da tiho spoji tuđi kontekst.</summary>
    public static BookingExecutionContext ForBooking(Appointment appointment, Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        AppointmentExecutionContext execution = ForAppointment(appointment);

        if (!booking.Id.HasValue)
            throw new InvalidOperationException("Izvršni kontekst zahtijeva booking s dodijeljenim Id-em.");
        if (booking.OrganizationId != execution.OrganizationId)
            throw new InvalidOperationException("Booking ne pripada organizaciji termina.");
        if (booking.AppointmentId != execution.AppointmentId)
            throw new InvalidOperationException("Booking ne pripada zadanom terminu.");

        return new BookingExecutionContext(execution, booking.Id.Value, booking.ClientId);
    }
}
