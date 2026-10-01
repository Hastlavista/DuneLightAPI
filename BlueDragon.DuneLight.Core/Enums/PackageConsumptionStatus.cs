namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase D3B3A — stanje jednog zapisa potrošnje paketa (PackageConsumption). Consumed: entitlement paketa je
/// primijenjen na sudjelovanje. Reversed: ta potrošnja je poništena (ulazak vraćen) — zapis se NIKAD ne briše, nova
/// potrošnja nakon poništenja je NOVI zapis (isti ledger obrazac kao CommissionEntryStatus Earned/Reversed).
/// </summary>
public enum PackageConsumptionStatus
{
    Consumed,
    Reversed
}
