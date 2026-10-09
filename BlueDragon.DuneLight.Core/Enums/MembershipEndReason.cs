namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2B) — zašto članstvo ima zakazan ili nastupio završetak (ends_on).</summary>
public enum MembershipEndReason
{
    Cancelled,
    EndOverride,
    PlanDeactivated,
    NonPayment,

    /// <summary>K1-8 — otkaz dok članstvo stoji zbog zatvorenih poslovnica: završava odmah, bez otkaznog roka i obveze.</summary>
    CancelledDuringCompanyClosure
}
