using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Postavke fonda godišnjeg odmora po zaposleniku (broj dana, datum obnove, datum isteka prijenosa) — admin only, nema own/all podjele (isti obrazac kao WorkingHoursTemplate).</summary>
[ApiController]
[Route("api/employees/{employeeId:guid}/leave-settings")]
[Produces("application/json")]
public class EmployeeLeaveSettingsController : ControllerBase
{
    private readonly IEmployeeLeaveSettingsService _employeeLeaveSettingsService;

    public EmployeeLeaveSettingsController(IEmployeeLeaveSettingsService employeeLeaveSettingsService)
    {
        _employeeLeaveSettingsService = employeeLeaveSettingsService;
    }

    [HttpGet]
    [RequireGrant(Grants.RosterLeaveFundSettingsView)]
    public async Task<ActionResult<EmployeeLeaveSettingsDto>> Get(Guid employeeId)
    {
        return Ok(await _employeeLeaveSettingsService.GetForEmployee(this.CurrentOrganizationId(), employeeId));
    }

    [HttpPut]
    [RequireGrant(Grants.RosterLeaveFundSettingsManage)]
    public async Task<ActionResult<EmployeeLeaveSettingsDto>> Upsert(Guid employeeId, [FromBody] EmployeeLeaveSettingsUpsertRequest request)
    {
        return Ok(await _employeeLeaveSettingsService.Upsert(this.CurrentOrganizationId(), this.CurrentUserId(), employeeId, request));
    }
}
