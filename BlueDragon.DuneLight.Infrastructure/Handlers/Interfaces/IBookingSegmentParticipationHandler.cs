using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

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

    /// <summary>Phase M0: termin adresiranog sudjelovanja (participation-native naredba zaključava termin PRIJE
    /// sudjelovanja). Null ako sudjelovanje ne postoji u organizaciji.</summary>
    Task<Guid?> GetAppointmentIdOf(Guid organizationId, Guid participationId);

    /// <summary>Phase M0: zaključava retke sudjelovanja (SELECT ... FOR UPDATE) u STABILNOM redoslijedu (po Id-u) unutar
    /// pozivateljeve transakcije — dvije transakcije koje zaključavaju preklapajuće skupove ne mogu se zaključati u krug.
    /// Svaka izvršna/komercijalna mutacija sudjelovanja (prijelaz statusa, plaćanje, paket, checkout) serijalizira se na
    /// OVOM retku; Booking redak se više ne zaključava (Booking nema vlastito promjenjivo stanje).</summary>
    Task LockForUpdate(IUnitOfWork uow, Guid organizationId, IEnumerable<Guid> participationIds, CancellationToken cancellationToken = default);

    /// <summary>Phase M0: zaključava JEDNO sudjelovanje pa (praćeno, u istoj transakciji) učitava njegov Booking sa svim
    /// sudjelovanjima (i njihovim potrošnjama paketa) — svježe stanje pod lockom. Null ako sudjelovanje ne postoji u organizaciji.</summary>
    Task<Booking> GetBookingWithLockedParticipation(
        IUnitOfWork uow, Guid organizationId, Guid participationId, CancellationToken cancellationToken = default);

    /// <summary>Phase M0: isto za Booking-wide naredbu — zaključava SVA sudjelovanja Bookinga (redoslijed po Id-u) pa
    /// učitava Booking kao gore. Null ako Booking ne postoji u organizaciji.</summary>
    Task<Booking> GetBookingWithLockedParticipations(
        IUnitOfWork uow, Guid organizationId, Guid bookingId, CancellationToken cancellationToken = default);
    Task<List<BookingSegmentParticipation>> GetForBooking(Guid organizationId, Guid bookingId);
    Task<List<BookingSegmentParticipation>> GetForSegment(Guid organizationId, Guid appointmentSegmentId);

    /// <summary>Postoji li ijedno sudjelovanje na bilo kojem Bookingu termina — povijest koja blokira trajno brisanje.</summary>
    Task<bool> ExistsForAppointment(Guid organizationId, Guid appointmentId);
}
