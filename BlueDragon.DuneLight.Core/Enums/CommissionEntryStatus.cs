namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Individualni Booking completion IMA reverzijsku putanju: BookingService.ApplyIndividualCompletionCorrection
/// (Individual Booking Completed -&gt; Confirmed korekcija) prebacuje odgovarajući Earned zapis u Reversed u istoj
/// transakciji (vidi CommissionEntry.cs SourceVersion domensku napomenu). Grupni Appointment completion i
/// Checkout (Product/Package sale) completion i dalje NEMAJU legitiman put natrag (vidi CommissionService
/// domensku napomenu) — Reversed za te izvore ostaje bez pozivatelja. CommissionEntry retke NIKAD se ne briše —
/// reverzija mijenja Status (+ReversedAt/ReversedBy), ne uklanja redak.
/// </summary>
public enum CommissionEntryStatus
{
    Earned,
    Reversed
}
