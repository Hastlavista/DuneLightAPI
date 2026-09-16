namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// MVP podržava samo dva izračuna — vidi CommissionRule.Value. Percentage zahtijeva 0-100, Fixed zahtijeva
/// samo &gt;= 0 (vidi CommissionRuleService validaciju). Bez tiered/progressive/threshold u ovoj fazi.
/// </summary>
public enum CommissionCalculationType
{
    Percentage,
    Fixed
}
