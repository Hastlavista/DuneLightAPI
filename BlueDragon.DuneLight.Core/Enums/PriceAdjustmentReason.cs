namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2E) — zašto se pogodnost članarine ne primjenjuje na sesiju (uz NotApplicable).</summary>
public enum PriceAdjustmentReason
{
    /// <summary>Sesija je pokrivena članarinom — cijena je cjenik, dug 0 (Q2, Q9).</summary>
    CoveredByMembership,
    /// <summary>Sesija je pokrivena paketom (Q2).</summary>
    CoveredByPackage,
    /// <summary>Plan nema pravilo pogodnosti za ovu uslugu.</summary>
    NoBenefitForService,
    CompanyNotCovered,
    Paused,
    DebtNotCovered,
    /// <summary>Članstvo ne vrijedi na datum sesije.</summary>
    OutsideMembershipPeriod,
    /// <summary>Cijena sesije nije razriješena iz cjenika (nema osnovice za pogodnost).</summary>
    NoBasePrice
}
