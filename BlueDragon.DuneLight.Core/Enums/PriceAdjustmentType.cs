namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q1, §10.4) — izvor prilagodbe cijene sesije. Redoslijed vrijednosti je tie-breaker kod iste cijene (trajni odnos
/// ispred povremene pogodnosti, promo zadnji). U P2 postoji samo izvor Membership; ostali su šav za buduću fazu pogodnosti.</summary>
public enum PriceAdjustmentType
{
    Membership,
    ClientTag,
    ClientGroup,
    Promo
}
