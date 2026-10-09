using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authentication;
using BlueDragon.DuneLight.API.TestTools;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Management;

/// <summary>
/// T1 — PRIVREMENI testni alati u Management portalu (samo platformski operater; ruta postoji samo uz TestTools:Enabled, inače
/// 404). Simulirani pomak poslovnog sata organizacije (samo naprijed, najviše 400 dana po skoku; propušteni poslovi se izvršavaju
/// dan po dan), stanje i seed (T1-4: nova demo organizacija, reset, dopuna postojeće — samo dodaje). Korisnici studija ne mogu pomicati vrijeme; u aplikaciji vide samo traku simuliranog vremena.
/// U organizaciji koja nije demo pomak je TRAJAN (Management upozorava prije potvrde). Uklanja se prije go-livea.
/// </summary>
[ApiController]
[TestToolsOnly]
[Route("api/management")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = PlatformAuthenticationDefaults.Scheme)]
public class ManagementTestToolsController : ControllerBase
{
    private readonly ITestToolsService _testToolsService;
    private readonly IDemoSeedService _demoSeedService;

    public ManagementTestToolsController(ITestToolsService testToolsService, IDemoSeedService demoSeedService)
    {
        _testToolsService = testToolsService;
        _demoSeedService = demoSeedService;
    }

    [HttpGet("organizations/{organizationId:guid}/test-tools")]
    public async Task<ActionResult<TestToolsOrganizationStatusDto>> GetStatus(Guid organizationId)
    {
        return Ok(await _testToolsService.GetStatus(organizationId));
    }

    /// <summary>Pomak sata naprijed (Days ili To). Odmah izvršava prolaz obnove za svaki preskočeni dan.</summary>
    [HttpPost("organizations/{organizationId:guid}/test-tools/clock/advance")]
    public async Task<ActionResult<TestClockAdvanceResultDto>> AdvanceClock(Guid organizationId, [FromBody] TestClockAdvanceRequest request)
    {
        return Ok(await _testToolsService.AdvanceClock(organizationId, request, CurrentPlatformAccountId()));
    }

    /// <summary>T1-4 — nova demo organizacija razine "Osnova": poslovnice s radnim vremenom i praznikom, zaposlenici, korisnici
    /// s grupama ovlasti (i jedan bez ovlasti), klijenti. Bez kataloga, rasporeda i naplate. Lozinke se vraćaju samo ovdje.</summary>
    [HttpPost("test-tools/demo-organizations/basic")]
    public async Task<ActionResult<DemoOrganizationResultDto>> CreateBasicDemoOrganization()
    {
        return Ok(await _demoSeedService.CreateDemoOrganization(DemoSeedLevel.Basic));
    }

    /// <summary>T1-4 — nova demo organizacija razine "Puni demo": sve iz "Osnove" + katalog, članarine u svim stanjima, paketi,
    /// raspored tekućeg i sljedećeg tjedna s prisutnošću, checkouti s provizijama. Stanja iz prošlosti nastaju skokom sata, pa
    /// organizacija završava sa simuliranim vremenom. Lozinke se vraćaju samo ovdje.</summary>
    [HttpPost("test-tools/demo-organizations/full")]
    public async Task<ActionResult<DemoOrganizationResultDto>> CreateFullDemoOrganization()
    {
        return Ok(await _demoSeedService.CreateDemoOrganization(DemoSeedLevel.Full));
    }

    /// <summary>T1-4 — stara ruta (prije razina), zadržana radi kompatibilnosti: isto kao <c>demo-organizations/full</c>.</summary>
    [HttpPost("test-tools/demo-organizations")]
    public async Task<ActionResult<DemoOrganizationResultDto>> CreateDemoOrganization()
    {
        return Ok(await _demoSeedService.CreateDemoOrganization(DemoSeedLevel.Full));
    }

    /// <summary>T1-4 — reset demo organizacije: nova demo organizacija ISTE razine (pomak sata 0 prije seeda, svježi seed), stara
    /// se umirovljuje (korisnici deaktivirani). Samo za demo organizaciju, inače TEST_TOOLS_NOT_DEMO_ORGANIZATION.</summary>
    [HttpPost("test-tools/demo-organizations/{organizationId:guid}/reset")]
    public async Task<ActionResult<DemoOrganizationResultDto>> ResetDemoOrganization(Guid organizationId)
    {
        return Ok(await _demoSeedService.ResetDemoOrganization(organizationId));
    }

    /// <summary>T1-4 — dopuna postojeće organizacije testnim podacima: samo dodaje, ništa postojeće ne mijenja ni ne briše.</summary>
    [HttpPost("organizations/{organizationId:guid}/test-tools/seed")]
    public async Task<ActionResult<DemoSeedResultDto>> SeedOrganization(Guid organizationId)
    {
        return Ok(await _demoSeedService.SeedExistingOrganization(organizationId));
    }

    private Guid? CurrentPlatformAccountId()
    {
        string value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(value, out Guid id) ? id : null;
    }
}
