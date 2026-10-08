namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2B) — IZVEDENO stanje članstva (nikad se ne sprema), na dan u zoni organizacije (Q22): Voided → Ended
/// (ends_on prošao) → Scheduled (početak u budućnosti) → Paused (pauza pokriva dan) → Active.</summary>
public enum MembershipState
{
    Scheduled,
    Active,
    Paused,
    Ended,
    Voided
}
