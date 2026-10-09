using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.TestTools;

// T1-4 — PRIVREMENI testni alat (seed iz Managementa, samo uz TestTools:Enabled), uklanja se prije go-livea.

/// <summary>Razina nove demo organizacije (pamti se na organizaciji; reset stvara organizaciju iste razine).</summary>
public enum DemoSeedLevel
{
    /// <summary>"Osnova": poslovnice s radnim vremenom i jednim praznikom, zaposlenici, korisnici s grupama ovlasti (i jedan bez
    /// ovlasti), klijenti s GDPR suglasnošću i rođendanima. Bez usluga, cjenika, paketa, članarina, grupa, termina i checkouta.</summary>
    Basic,

    /// <summary>"Puni demo": sve iz "Osnove" + katalog, članarine u svim stanjima, paketi klijenata, grupe i raspored tekućeg i
    /// sljedećeg tjedna s prisutnošću, checkouti s provizijama; stanja koja traže prošlost nastaju skokom sata.</summary>
    Full
}

/// <summary>Koliko je čega seed DODAO (bez detaljnog izvještaja, T1 odluka). Postojeći podaci se nikad ne mijenjaju.</summary>
public class DemoSeedCountsDto
{
    public int Companies { get; set; }
    public int CompanyHolidays { get; set; }
    public int Rooms { get; set; }
    public int Resources { get; set; }
    public int Services { get; set; }
    public int PriceListItems { get; set; }
    public int Packages { get; set; }
    public int MembershipPlans { get; set; }
    public int CancellationPolicies { get; set; }
    public int CancellationReasons { get; set; }
    public int EngagementTypes { get; set; }
    public int GrantGroups { get; set; }
    public int Employees { get; set; }
    public int Clients { get; set; }
    public int Groups { get; set; }
    public int GroupMembers { get; set; }
    public int GroupAppointments { get; set; }

    /// <summary>Zabilježene prisutnosti članova na prošlim grupnim terminima (samo "Puni demo").</summary>
    public int GroupAttendances { get; set; }
    public int PastAppointments { get; set; }
    public int FutureAppointments { get; set; }
    public int CommissionRules { get; set; }

    /// <summary>Prodana članstva ukupno; stanja ispod se na kraju seeda čitaju iz servisa (ne pretpostavljaju se).</summary>
    public int MembershipsSold { get; set; }

    /// <summary>Aktivna i bez otvorenog duga.</summary>
    public int MembershipsActivePaid { get; set; }

    /// <summary>Aktivna, s neplaćenim zaduženjem koje još nije dug (prije isteka grace perioda).</summary>
    public int MembershipsAwaitingPayment { get; set; }

    /// <summary>Dug nakon grace perioda (Standing = Delinquent).</summary>
    public int MembershipsInDebt { get; set; }
    public int MembershipsPaused { get; set; }
    public int MembershipsEnded { get; set; }

    /// <summary>Članstvo stoji jer je poslovnica opsega plana zatvorena (K1-8, sustavna pauza CompanyClosure).</summary>
    public int MembershipsStandingStill { get; set; }

    /// <summary>Paketi prodani klijentima kroz checkout.</summary>
    public int ClientPackagesSold { get; set; }

    /// <summary>Jedinice paketa potrošene odrađenim sesijama.</summary>
    public int PackageUnitsConsumed { get; set; }
    public int CheckoutsCompleted { get; set; }

    /// <summary>Zapisi provizija seedanih zaposlenika (odrađene usluge, grupni termini, prodaja paketa i članarina).</summary>
    public int CommissionEntries { get; set; }
}

/// <summary>Testni korisnik s loginom. Lozinka se vraća SAMO u ovom odgovoru (nigdje se drugdje ne sprema).</summary>
public class DemoSeedUserDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }

    /// <summary>Kratak opis ovlasti (npr. "Admin grupa — svi grantovi").</summary>
    public string Description { get; set; }
}

/// <summary>Rezultat dopune organizacije: oznaka pokretanja (sufiks naziva i emailova), dodani korisnici i brojevi.</summary>
public class DemoSeedResultDto
{
    public Guid OrganizationId { get; set; }

    /// <summary>Sufiks dodan svakom nazivu/emailu ovog pokretanja (npr. "a1b2c3"), da ponovljena pokretanja ne sudaraju.</summary>
    public string RunTag { get; set; }

    public List<DemoSeedUserDto> Users { get; set; } = new();
    public DemoSeedCountsDto Counts { get; set; } = new();

    /// <summary>Što seed nije dodao, s razlogom: odbijeni opcionalni dijelovi (npr. pojedini termin) i, kod dopune postojeće
    /// organizacije, stanja koja traže protok vremena (tamo se sat ne pomiče).</summary>
    public List<string> Skipped { get; set; } = new();

    /// <summary>Za koliko je dana seed pomaknuo sat organizacije (0 = nije; pomiče ga samo "Puni demo").</summary>
    public int ClockAdvancedDays { get; set; }

    /// <summary>Poslovni "danas" organizacije na kraju seeda (nakon eventualnog skoka sata).</summary>
    public DateOnly LocalDate { get; set; }
}

/// <summary>Nova demo organizacija (ili rezultat reseta): organizacija, osnivač i testni korisnici, brojevi.</summary>
public class DemoOrganizationResultDto : DemoSeedResultDto
{
    public string Name { get; set; }
    public string Slug { get; set; }
    public DemoSeedLevel Level { get; set; }

    /// <summary>Samo kod reseta: stara demo organizacija koja je umirovljena (korisnici deaktivirani).</summary>
    public Guid? RetiredOrganizationId { get; set; }
}
