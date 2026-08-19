using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Predložak radnog vremena (zaposlenik/lokacija) i izračun dostupnosti — FAZA 1/2. Uređivanje je namjerno
/// Admin/Owner-only (roster.templates.manage, bez own/all) jer generira tvrdu blokadu zakazivanja za sve role.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class WorkingHoursTemplatesController : ControllerBase
{
    private readonly IWorkingHoursTemplateService _workingHoursTemplateService;

    public WorkingHoursTemplatesController(IWorkingHoursTemplateService workingHoursTemplateService)
    {
        _workingHoursTemplateService = workingHoursTemplateService;
    }

    [HttpGet("employees/{employeeId:guid}/working-hours-template")]
    [RequireGrant(Grants.RosterTemplatesView, Grants.RosterTemplatesManage)]
    public async Task<ActionResult<WorkingHoursTemplateDto>> GetForEmployee(Guid employeeId)
    {
        return Ok(await _workingHoursTemplateService.GetForEmployee(this.CurrentOrganizationId(), employeeId));
    }

    [HttpPut("employees/{employeeId:guid}/working-hours-template")]
    [RequireGrant(Grants.RosterTemplatesManage)]
    public async Task<ActionResult<WorkingHoursTemplateDto>> UpsertForEmployee(
        Guid employeeId, [FromBody] WorkingHoursTemplateUpsertRequest request)
    {
        return Ok(await _workingHoursTemplateService.UpsertForEmployee(
            this.CurrentOrganizationId(), this.CurrentUserId(), employeeId, request));
    }

    [HttpGet("companies/{companyId:guid}/working-hours-template")]
    [RequireGrant(Grants.RosterTemplatesView, Grants.RosterTemplatesManage)]
    public async Task<ActionResult<WorkingHoursTemplateDto>> GetForCompany(Guid companyId)
    {
        return Ok(await _workingHoursTemplateService.GetForCompany(this.CurrentOrganizationId(), companyId));
    }

    [HttpPut("companies/{companyId:guid}/working-hours-template")]
    [RequireGrant(Grants.RosterTemplatesManage)]
    public async Task<ActionResult<WorkingHoursTemplateDto>> UpsertForCompany(
        Guid companyId, [FromBody] WorkingHoursTemplateUpsertRequest request)
    {
        return Ok(await _workingHoursTemplateService.UpsertForCompany(
            this.CurrentOrganizationId(), this.CurrentUserId(), companyId, request));
    }

    /// <summary>Nacrt iz FAZE 1 — dostupnost za jedan dan, EffectiveIntervals = presjek employee ∩ company (za buduće "pronađi slobodan termin").</summary>
    [HttpGet("availability")]
    [RequireGrant(Grants.RosterTemplatesView, Grants.RosterTemplatesManage, Grants.AppointmentsWriteOwn, Grants.AppointmentsWriteAll)]
    public async Task<ActionResult<AvailabilityDto>> GetAvailability(
        [FromQuery] Guid employeeId, [FromQuery] Guid companyId, [FromQuery] DateTimeOffset date)
    {
        return Ok(await _workingHoursTemplateService.GetAvailability(this.CurrentOrganizationId(), employeeId, companyId, date));
    }
}