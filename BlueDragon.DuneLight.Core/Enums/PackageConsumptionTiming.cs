namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase D3B3A — postavka organizacije: u kojem trenutku životnog ciklusa sudjelovanja se paket troši. Trenutna
/// aplikacija podržava JEDNO ponašanje: OnCompletion — paket se troši tek kad sudjelovanje prijeđe u Completed
/// (odrađivanje sudjelovanja, CompleteNow, grupni check-in); odabir paketa na Confirmed bookingu nema učinka do tada.
/// Druge vrijednosti se namjerno ne uvode dok ne postoji stvarno podržano ponašanje.
/// </summary>
public enum PackageConsumptionTiming
{
    OnCompletion
}
