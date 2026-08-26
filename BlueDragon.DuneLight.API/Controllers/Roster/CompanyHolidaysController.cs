using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Neradni dani poslovnice — dio radno-vremenskog mehanizma (vidi WorkingHoursCalculator), zato
/// ponovno koristi roster.templates.view/.manage umjesto zasebnog kataloškog granta.</summary>
[ApiController]
[Route("api/companies/{companyId:guid}/holidays")]
[Produces("application/json")]
public class CompanyHolidaysController : ControllerBase
{
    private readonly ICompanyHolidayService _companyHolidayService;

    public CompanyHolidaysController(ICompanyHolidayService companyHolidayService)
    {
        _companyHolidayService = companyHolidayService;
    }

    [HttpGet]
    [RequireGrant(Grants.RosterTemplatesView, Grants.RosterTemplatesManage)]
    public async Task<ActionResult<List<CompanyHolidayDto>>> GetForCompany(Guid companyId, [FromQuery] int year)
    {
        return Ok(await _companyHolidayService.GetForCompany(this.CurrentOrganizationId(), companyId, year));
    }

    [HttpPost]
    [RequireGrant(Grants.RosterTemplatesManage)]
    public async Task<ActionResult<CompanyHolidayDto>> Create(Guid companyId, [FromBody] CompanyHolidayCreateRequest request)
    {
        CompanyHolidayDto created = await _companyHolidayService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), companyId, request);
        return CreatedAtAction(nameof(GetForCompany), new { companyId, year = created.Date.Year }, created);
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.RosterTemplatesManage)]
    public async Task<IActionResult> Delete(Guid companyId, Guid id)
    {
        await _companyHolidayService.Delete(this.CurrentOrganizationId(), companyId, id);
        return NoContent();
    }

    [HttpPost("generate")]
    [RequireGrant(Grants.RosterTemplatesManage)]
    public async Task<ActionResult<GenerateCompanyHolidaysResult>> Generate(Guid companyId, [FromQuery] int year)
    {
        return Ok(await _companyHolidayService.Generate(this.CurrentOrganizationId(), this.CurrentUserId(), companyId, year));
    }
}
