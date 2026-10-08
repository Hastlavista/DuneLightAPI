namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q4) — ponašanje kad je limit članarine iskorišten (postavka organizacije): rezervacija prolazi i ide na
/// sljedeći izvor (članarina → paket → normalan settlement) uz upozorenje (default), ili se odbija (MEMBERSHIP_LIMIT_EXCEEDED).
/// Automatski procesi (generiranje grupe, ponovna evaluacija) nikad ne odbijaju.</summary>
public enum MembershipLimitExceededBehavior
{
    FallbackToNextSource,
    Reject
}
