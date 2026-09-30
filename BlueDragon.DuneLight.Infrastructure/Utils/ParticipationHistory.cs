using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase D3B1 — JEDINO mjesto koje definira kada se Booking + njegovo sudjelovanje smiju fizički obrisati (zaključano
/// pravilo): samo dok sudjelovanje NEMA izvršnu povijest, tj. je "netaknuto" — Status == Confirmed, StatusVersion == 0,
/// bez dolaska (ArrivedAt/ArrivedBy), bez razloga otkazivanja i bez klasifikacije kasnog otkazivanja. Sam trenutni status
/// nije dovoljan: Confirmed → Completed → ispravak na Confirmed ima StatusVersion &gt; 0 i NIJE netaknuto.
///
/// Koriste ga sva tri postojeća toka fizičkog brisanja: izostavljeni Confirmed klijent kod Update i CompleteExisting
/// (AppointmentHandler.UpdateWithBookingsCore) te brisanje termina istog dana (AppointmentHandler.Delete). Brisanje je
/// UVIJEK eksplicitno (sudjelovanje pa Booking), nikad kaskadom; sudjelovanje s poviješću → REFERENCED_CANNOT_DELETE.
/// </summary>
public static class ParticipationHistory
{
    public static bool IsUntouched(BookingSegmentParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(participation);
        return participation.Status == ParticipationStatus.Confirmed
               && participation.StatusVersion == 0
               && participation.ArrivedAt == null
               && participation.ArrivedBy == null
               && participation.CancellationReason == null
               && participation.IsLateCancellation == null;
    }

    /// <summary>Označava za brisanje (unutar pozivateljevog SaveChanges) svako sudjelovanje pa Booking — ili baca
    /// REFERENCED_CANNOT_DELETE (i ne označava ništa) ako bilo koje sudjelovanje ima povijest.</summary>
    public static void RemoveUntouched(DatabaseContext context, IReadOnlyCollection<Booking> bookings, string message)
    {
        List<BookingSegmentParticipation> participations = bookings.SelectMany(b => b.Participations).ToList();
        if (participations.Any(p => !IsUntouched(p)))
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, message);

        context.BookingSegmentParticipations.RemoveRange(participations);
        context.Bookings.RemoveRange(bookings);
    }
}
