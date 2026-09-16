namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Earned je jedini status koji ovaj MVP trenutno proizvodi — nijedan postojeći poslovni prijelaz (individualni
/// Booking completion, grupni Appointment completion, Checkout completion) nema legitiman put natrag na
/// ne-odrađeno/ne-prodano stanje (vidi CommissionService domensku napomenu), pa Reversed ostaje rezerviran za
/// buduću korekcijsku putanju umjesto da se izmišlja jedna koja danas ne postoji (vidi spec section 39).
/// CommissionEntry retke NIKAD se ne briše — reverzija (kad zaživi) mijenja Status, ne uklanja redak.
/// </summary>
public enum CommissionEntryStatus
{
    Earned,
    Reversed
}
