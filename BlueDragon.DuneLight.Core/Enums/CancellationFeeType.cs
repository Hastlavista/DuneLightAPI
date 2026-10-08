namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P1 (ADR-0017, D4) — oblik naknade jednog događaja politike: bez naknade, fiksni iznos (&gt;= 0) ili postotak
/// (0–100) konačne cijene sudjelovanja; naknada je uvijek ograničena konačnom cijenom.</summary>
public enum CancellationFeeType
{
    None,
    Fixed,
    Percentage
}
