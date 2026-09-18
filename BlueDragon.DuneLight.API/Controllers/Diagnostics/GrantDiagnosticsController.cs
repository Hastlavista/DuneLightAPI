using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;
using BlueDragon.DuneLight.Core.Interfaces.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace BlueDragon.DuneLight.API.Controllers.Diagnostics;

/// <summary>
/// FAZA 1 Part H — najsigurnija dostupna izloženost dok ne postoji poseban platform-admin autorizacijski model:
/// dvostruka brana, [RequireOwner] (opcija C) I dostupno isključivo kad je host Development okruženje (opcija B).
/// U Production/Staging vraća 404 bez obzira na autentikaciju, pa izvještaj (koji sadrži rute i auth metapodatke)
/// nikad nije dohvatljiv iz stvarnog tenant okruženja. Ruta je namjerno pod "_internal" prefiksom, ne pod
/// api/permissions/*, kako se ne bi doimala kao redovna tenant-admin značajka.
/// </summary>
[ApiController]
[Route("api/_internal/diagnostics/grants")]
[Produces("application/json")]
[RequireOwner]
public class GrantDiagnosticsController : ControllerBase
{
    private readonly IGrantDiagnosticsService _grantDiagnosticsService;
    private readonly IHostEnvironment _hostEnvironment;

    public GrantDiagnosticsController(IGrantDiagnosticsService grantDiagnosticsService, IHostEnvironment hostEnvironment)
    {
        _grantDiagnosticsService = grantDiagnosticsService;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet]
    public async Task<ActionResult<GrantDiagnosticsReport>> GetReport()
    {
        if (!_hostEnvironment.IsDevelopment())
            return NotFound();

        return Ok(await _grantDiagnosticsService.GenerateReport());
    }
}
