namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2E) — zašto se cijena sesije ne mijenja automatski uz promjenu pokrića/pogodnosti: ručno postavljen iznos ili
/// aktivna novčana alokacija (sesija zadržava cijenu po kojoj je plaćena).</summary>
public enum PriceProtectionReason
{
    ManualAmount,
    AlreadyPaid
}
