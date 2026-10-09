namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2B) — IZVEDENO stanje članstva (nikad se ne sprema), na dan u zoni organizacije (Q22): Voided → Ended
/// (ends_on prošao) → Scheduled (početak u budućnosti) → StandingStill (K1-8) → Paused (pauza pokriva dan) → Active.</summary>
public enum MembershipState
{
    Scheduled,
    Active,
    Paused,
    Ended,
    Voided,

    /// <summary>K1-8 — članstvo stoji jer su sve poslovnice opsega plana zatvorene (sustavna pauza CompanyClosure, ne
    /// klijentova); datum od kad je u ClientMembershipDto.StandingStillSince.</summary>
    StandingStill
}
