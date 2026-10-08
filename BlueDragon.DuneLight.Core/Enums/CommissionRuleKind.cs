namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2F, §18.1) — vrsta pravila provizije: za odrađeno (izvođač sesije, trener × usluga) ili za prodaju (korisnik
/// odabran na stavci naplate). Dvije odvojene provizije koje mogu postojati na istoj stvari.</summary>
public enum CommissionRuleKind
{
    Performance,
    Sale
}
