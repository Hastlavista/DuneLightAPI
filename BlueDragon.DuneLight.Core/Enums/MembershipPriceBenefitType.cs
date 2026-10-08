namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2E) — vrsta cjenovne pogodnosti: postotak popusta (0–100], fiksni iznos popusta (&gt; 0) ili fiksna cijena za člana
/// (&gt;= 0). Rezultat se zaokružuje na 0,01 i nikad nije ispod 0.</summary>
public enum MembershipPriceBenefitType
{
    PercentOff,
    AmountOff,
    FixedPrice
}
