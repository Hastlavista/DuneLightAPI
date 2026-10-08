namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q1, §10.4) — ishod jednog kandidata prilagodbe u evaluaciji cijene sesije (objašnjivost).</summary>
public enum PriceAdjustmentOutcome
{
    /// <summary>Najbolja cijena — primijenjena.</summary>
    Applied,
    /// <summary>Drugi kandidat daje nižu cijenu.</summary>
    LostToBetterPrice,
    /// <summary>Ista cijena kao primijenjeni kandidat; odlučio fiksni redoslijed tipova/izvora.</summary>
    LostOnTie,
    /// <summary>Pogodnost ne snižava cijenu (npr. fiksna cijena za člana viša od cjenika).</summary>
    NoReduction,
    /// <summary>Pogodnost se ne primjenjuje (razlog u Reason).</summary>
    NotApplicable
}
