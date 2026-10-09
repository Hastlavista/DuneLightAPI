namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>T1-8 — segmentna naredba koja je ponovno pročitala cjenik i promijenila cijenu sudjelovanja
/// (upozorenje PARTICIPATION_PRICE_CHANGED).</summary>
public enum ParticipationPriceChangeReason
{
    /// <summary>Promijenjena usluga segmenta.</summary>
    Service,

    /// <summary>Promijenjen izvor cijene segmenta (PricingMode/PricingEmployeeId).</summary>
    PricingSource,

    /// <summary>Promjena zaposlenika segmenta promijenila je efektivni izvor cijene.</summary>
    Employees,

    /// <summary>Novi početak segmenta pada na drugi dan cjenika (lokalni datum u zoni poslovnice).</summary>
    Time
}
