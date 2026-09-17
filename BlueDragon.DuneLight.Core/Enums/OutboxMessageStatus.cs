namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Pending -> eligible za obradu. Processing -> leasan od strane jednog workera (vidi OutboxMessage.LockedUntil
/// za lease-recovery). Processed/Failed su terminalni — Failed nakon iscrpljenih pokušaja, obje se čuvaju kao
/// povijest (bez fizičkog brisanja, vidi spec section 5).
/// </summary>
public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
