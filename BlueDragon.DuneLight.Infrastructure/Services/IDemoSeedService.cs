using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.TestTools;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// T1-4 — PRIVREMENI testni alat, uklanja se prije go-livea (samo uz TestTools:Enabled, poziva ga Management).
/// Seed testnih podataka kroz postojeće aplikacijske servise (u duhu ADR-0022): nova demo organizacija razine "Osnova" ili
/// "Puni demo" (s resetom iste razine) ili dopuna postojeće organizacije. Seed SAMO DODAJE: nikad ne mijenja ni ne briše
/// postojeće poslovnice, radno vrijeme, usluge, grupe, cjenike, grupe ovlasti, postavke ni korisnike. Svaki naziv/email dobiva
/// kratak sufiks pokretanja (" #a1b2c3"), pa ponovljeno pokretanje ne sudara s jedinstvenim nazivima. Odgovor = koliko je čega
/// dodano (bez detaljnog izvještaja). Seed nije jedna transakcija: pri prekidu napraviti novu demo organizaciju.
/// </summary>
public interface IDemoSeedService
{
    /// <summary>Registrira novu organizaciju (isti tok kao javna registracija), označava je kao demo zadane razine i puni je.
    /// "Puni demo" pomiče sat organizacije (skok kroz ITestToolsService.AdvanceClock), pa završava sa simuliranim vremenom.</summary>
    Task<DemoOrganizationResultDto> CreateDemoOrganization(DemoSeedLevel level);

    /// <summary>Reset demo organizacije = nova demo organizacija ISTE razine (svježi seed); stara se umirovljuje (korisnici
    /// deaktivirani). Samo za demo organizaciju koja nije već umirovljena, inače TEST_TOOLS_NOT_DEMO_ORGANIZATION.</summary>
    Task<DemoOrganizationResultDto> ResetDemoOrganization(Guid organizationId);

    /// <summary>Dopuna postojeće organizacije (samo dodaje, sat se nikad ne pomiče; stanja koja traže protok vremena idu u
    /// Skipped). Akter je aktivni korisnik organizacije u Admin grupi (<c>system_key = 'admin'</c>).</summary>
    Task<DemoSeedResultDto> SeedExistingOrganization(Guid organizationId);
}
