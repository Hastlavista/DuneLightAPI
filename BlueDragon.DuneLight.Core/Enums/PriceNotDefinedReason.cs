namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>T1-8 — zašto je cijena usluge uzeta iz zadane cijene usluge uz upozorenje PRICE_NOT_DEFINED (rupa u cjeniku).</summary>
public enum PriceNotDefinedReason
{
    /// <summary>Usluga ima stavke cjenika (u kontekstu razrješavanja), ali nijedna ne pokriva dan termina — korištena je zadana
    /// cijena usluge, bez obzira na iznos.</summary>
    NoPriceListItemForDate,

    /// <summary>Korištena zadana cijena usluge iznosi 0 € (nema oznake "besplatna usluga", pa se upozorava za svaki 0 €).</summary>
    ZeroDefaultPrice
}
