namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Phase M1G — izvor cijene segmenta (izvršne jedinice). NIJE vlasništvo, "glavni" zaposlenik niti korisnik provizije —
/// samo odgovor na pitanje "čiju cijenu zaposlenika cijena smije pokušati koristiti":
/// <list type="bullet">
/// <item><see cref="Standard"/> — razine cjenika zaposlenika se PRESKAČU (poslovnica+usluga → organizacija → zadana cijena);
/// PricingEmployeeId = null. Obavezno za segment bez zaposlenika.</item>
/// <item><see cref="Employee"/> — razine odabranog zaposlenika (PricingEmployeeId, član zaposlenika segmenta) imaju
/// prednost, uz isti pad na nezaposleničke razine. Automatski za segment s točno jednim zaposlenikom.</item>
/// </list>
/// Segment s 2+ zaposlenika zahtijeva EKSPLICITAN odabir — nikad se ne pogađa (prvi, najjeftiniji, prosjek...).
/// </summary>
public enum SegmentPricingMode
{
    Standard,
    Employee
}
