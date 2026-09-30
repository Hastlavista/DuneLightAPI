using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>
/// Phase D2 — JEDINA write-putanja za BookingSegmentParticipation (nema servisa ni produkcijskih pozivatelja; životni
/// ciklus SetStatus/Cancel/Complete/NoShow/CheckIn dolazi nakon prelaska na segmente). Svako čitanje je filtrirano po
/// OrganizationId.
/// </summary>
public interface IBookingSegmentParticipationHandler
{
    /// <summary>
    /// Sprema novo sudjelovanje nakon centralne validacije: Booking i segment postoje u organizaciji sudjelovanja
    /// (inače NotFound — i za entitet druge organizacije), pripadaju ISTOM terminu (inače
    /// PARTICIPATION_APPOINTMENT_MISMATCH), a par (Booking, segment) još ne postoji (inače DUPLICATE_PARTICIPATION; i
    /// jedinstveni indeks u bazi). Novo sudjelovanje uvijek počinje sa StatusVersion = 0 (nastanak nije prijelaz).
    /// </summary>
    Task Add(BookingSegmentParticipation participation);

    Task<BookingSegmentParticipation> GetById(Guid organizationId, Guid id);
    Task<List<BookingSegmentParticipation>> GetForBooking(Guid organizationId, Guid bookingId);
    Task<List<BookingSegmentParticipation>> GetForSegment(Guid organizationId, Guid appointmentSegmentId);

    /// <summary>Postoji li ijedno sudjelovanje na bilo kojem Bookingu termina — povijest koja blokira trajno brisanje.</summary>
    Task<bool> ExistsForAppointment(Guid organizationId, Guid appointmentId);
}
