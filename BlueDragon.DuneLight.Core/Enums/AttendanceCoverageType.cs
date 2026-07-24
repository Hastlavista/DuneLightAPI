namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// MonthlyPackage: klijent ima aktivan neograničen paket (relevantni brojač ulazaka je null) —
/// samo se bilježi prisutnost, ništa se ne skida.
/// SessionPackage: klijent ima paket s konačnim brojem ulazaka — jedan ulazak se skida.
/// SinglePaid: gost/probni bez odgovarajućeg paketa — naplata ide zasebno kroz postojeći unos
/// (usluga/paket "grupni trening x1"), ovdje se samo bilježi da je bio prisutan.
/// </summary>
public enum AttendanceCoverageType
{
    MonthlyPackage,
    SessionPackage,
    SinglePaid
}
