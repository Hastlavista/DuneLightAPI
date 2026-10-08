namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P1 (ADR-0017, D4) — događaj politike koji je stvorio posljedicu: kasno klijentsko otkazivanje ili izostanak.</summary>
public enum PolicyConsequenceEvent
{
    LateCancellation,
    NoShow
}
