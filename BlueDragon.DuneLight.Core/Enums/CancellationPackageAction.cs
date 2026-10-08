namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P1 (ADR-0017, D6) — smije li događaj politike umjesto naknade potrošiti jednu jedinicu brojenog paketa.
/// Jedinica i naknada su ALTERNATIVE, nikad zbroj.</summary>
public enum CancellationPackageAction
{
    None,
    ConsumeUnit
}
