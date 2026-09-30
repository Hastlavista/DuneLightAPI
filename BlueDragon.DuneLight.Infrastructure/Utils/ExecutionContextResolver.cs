using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje prevodi današnji jednostruki Appointment okvir (ServiceId/EmployeeId/StartsAt) u izvršni
/// kontekst za cijenu, pakete, proviziju, rok otkazivanja i blagajnu — komercijalna logika čita
/// <see cref="AppointmentExecutionContext"/>/<see cref="BookingExecutionContext"/>, ne Appointment polja izravno.
///
/// Čisto preslikavanje nad VEĆ učitanim entitetima, bez upita u bazu: pozivatelji rade unutar vlastite transakcije i
/// ponekad nad Appointmentom koji su upravo izmijenili (npr. CompleteExisting postavlja ServiceId/EmployeeId pa tek
/// onda zarađuje proviziju) — kontekst mora odražavati točno te vrijednosti, kao i prije. Kad se uvede
/// AppointmentSegment/BookingSegmentParticipation, ovdje se mijenja izvor, ne pozivatelji.
/// </summary>
public static class ExecutionContextResolver
{
    public static AppointmentExecutionContext ForAppointment(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (!appointment.Id.HasValue)
            throw new InvalidOperationException("Izvršni kontekst zahtijeva termin s dodijeljenim Id-em.");

        return new AppointmentExecutionContext(
            appointment.OrganizationId,
            appointment.Id.Value,
            appointment.CompanyId,
            appointment.ServiceId,
            appointment.Service?.Name,
            appointment.EmployeeId,
            appointment.StartsAt);
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
