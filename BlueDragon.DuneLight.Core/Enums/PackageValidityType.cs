namespace BlueDragon.DuneLight.Core.Enums;

public enum PackageValidityType
{
    /// <summary>Broj dana od datuma kupnje.</summary>
    DayCount,

    /// <summary>Istječe zadnji dan mjeseca u kojem je kupljen.</summary>
    EndOfMonth,

    /// <summary>Vrijedi do točno određenog datuma.</summary>
    FixedDate
}
