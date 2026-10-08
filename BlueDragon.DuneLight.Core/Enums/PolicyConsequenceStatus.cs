namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P1 (ADR-0017/ADR-0018) — stanje zapisa posljedice politike. Samo Active doprinosi dugu ili paketnom pokriću.
/// Waived = otpis cijele posljedice (nepovratno u P1); Reversed = poništeno korekcijom statusa. Zapis se nikad ne briše.
/// </summary>
public enum PolicyConsequenceStatus
{
    Active,
    Waived,
    Reversed
}
