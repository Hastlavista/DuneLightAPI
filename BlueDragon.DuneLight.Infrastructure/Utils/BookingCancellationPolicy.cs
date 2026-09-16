using System;

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
    public static bool IsLateCancellation(DateTimeOffset appointmentStartsAt, DateTimeOffset now, int cutoffMinutes)
    {
        return appointmentStartsAt - now < TimeSpan.FromMinutes(cutoffMinutes);
    }
}
