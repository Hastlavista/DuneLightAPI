namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2E) — opseg pravila cjenovne pogodnosti: izričito "sve usluge" (uključuje i kasnije dodane) ili jedna odabrana
/// usluga. Prazna usluga nikad ne znači "sve" (princip Q29).</summary>
public enum MembershipPriceBenefitScope
{
    AllServices,
    Service
}
