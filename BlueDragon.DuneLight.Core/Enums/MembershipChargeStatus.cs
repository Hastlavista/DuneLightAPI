namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q16) — izvedeni prikazni status zaduženja: lifecycle + plaćenost. Konačno = Paid ili WrittenOff.</summary>
public enum MembershipChargeStatus
{
    Pending,
    PartiallyPaid,
    Paid,
    WrittenOff,
    Voided
}
