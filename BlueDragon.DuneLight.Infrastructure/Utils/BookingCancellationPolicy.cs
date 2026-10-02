using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Fokusirani domenski evaluator za pitanja o vremenu otkazivanja — namjerno NE odlučuje o naknadama/povratu
/// paketa (to ostaje eksplicitna odluka pozivatelja, vidi BookingService), samo odgovara na činjenično pitanje
/// "je li ovo kasno otkazivanje". Cutoff dolazi iz OrganizationSettingsService (per-Organization), nikad
/// hardkodiran ovdje.
/// </summary>
public static class BookingCancellationPolicy
{
    /// <summary>Kasno otkazivanje = preostalo vrijeme do termina je STROGO manje od cutoffa. Točno na granici
    /// (== cutoff) se tretira kao normalno otkazivanje, ne kasno.</summary>
    public static bool IsLateCancellation(DateTimeOffset startsAt, DateTimeOffset now, int cutoffMinutes)
    {
        return startsAt - now < TimeSpan.FromMinutes(cutoffMinutes);
    }

    /// <summary>Phase M1E.1 — JEDINI ulaz za klasifikaciju otkazivanja SUDJELOVANJA, isti za sva tri opsega (jedno
    /// sudjelovanje, Booking-wide, cijeli termin): početak je PlannedStart SEGMENTA tog sudjelovanja (izvršni kontekst),
    /// nikad početak termina ni raspona — jedno otkazivanje termina može dati kasno A i pravovremeno B.</summary>
    public static bool IsLateCancellation(ParticipationExecutionContext participation, DateTimeOffset now, int cutoffMinutes)
    {
        ArgumentNullException.ThrowIfNull(participation);
        return IsLateCancellation(participation.StartsAt, now, cutoffMinutes);
    }
}
