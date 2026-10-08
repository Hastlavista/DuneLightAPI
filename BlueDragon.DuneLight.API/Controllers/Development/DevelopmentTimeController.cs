using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Development;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Development;

/// <summary>
/// RAZVOJNI ALAT (samo Development; izvan njega ruta ne postoji — <see cref="DevelopmentOnlyAttribute"/>). Simulacija vremena
/// za ručno testiranje članarina: pokreće isti prolaz obnove kao pozadinski servis (otvaranje perioda i zaduženja, istek grace
/// perioda i pravila duga, automatski završetak, usklađivanje pokrića i horizonta, naknadno dodavanje preskočenih članova grupe,
/// usklađivanje PriceStale) za trenutnu organizaciju, sa zadanim "danas" umjesto stvarnog datuma.
/// Ograničenja: čitanja (npr. Standing članstva) i ostala pravila i dalje koriste stvarni sat; pozadinski servis s pravim
/// datumom može vratiti stanje duga (za testiranje ga isključiti: MembershipRenewalSettings:Enabled=false).
/// </summary>
[ApiController]
[DevelopmentOnly]
[Route("api/dev/time")]
[Produces("application/json")]
public class DevelopmentTimeController : ControllerBase
{
    private readonly IMembershipRenewalService _renewalService;

    public DevelopmentTimeController(IMembershipRenewalService renewalService)
    {
        _renewalService = renewalService;
    }

    /// <summary>Prolaz obnove članarina za zadani lokalni datum organizacije (npr. "2026-11-15"). Idempotentno; može se pozivati
    /// redom za više dana. Vraća broj obrađenih članstava.</summary>
    [HttpPost("membership-renewal-run")]
    [RequireGrant(Grants.OrganizationSettingsManage)]
    public async Task<ActionResult<DevelopmentRenewalRunResult>> RunMembershipRenewal([FromBody] DevelopmentRenewalRunRequest request)
    {
        DateOnly date = request?.Date ?? throw new ValidationAppException("Date je obavezan (lokalni datum organizacije).");
        int processed = await _renewalService.RunForOrganization(this.CurrentOrganizationId(), date);
        return Ok(new DevelopmentRenewalRunResult { Date = date, Processed = processed });
    }
}

public class DevelopmentRenewalRunRequest
{
    public DateOnly? Date { get; set; }
}

public class DevelopmentRenewalRunResult
{
    public DateOnly Date { get; set; }
    public int Processed { get; set; }
}
