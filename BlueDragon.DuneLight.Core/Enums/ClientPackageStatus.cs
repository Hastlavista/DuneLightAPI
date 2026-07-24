namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Active/Cancelled su jedina eksplicitno postavljana stanja. Depleted se postavlja event-driven
/// kod skidanja zadnjeg ulaska. Expired se ne perzistira (nema background joba) — valjanost se
/// uvijek provjerava dinamički preko ExpiryDate u trenutku provjere.
/// </summary>
public enum ClientPackageStatus
{
    Active,
    Expired,
    Depleted,
    Cancelled
}
