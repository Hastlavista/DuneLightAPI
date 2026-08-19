using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Roster;

/// <summary>Fond godišnjeg odmora po zaposleniku — pregled stanja (Member smije samo svoj) i ručna admin korekcija/otvaranje po godini.</summary>
[ApiController]
[Route("api/employees/{employeeId:guid}/leave-funds")]
[Produces("application/json")]
public class LeaveFundsController : ControllerBase
{
    private readonly ILeaveFundService _leaveFundService;

    public LeaveFundsController(ILeaveFundService leaveFundService)
    {
        _leaveFundService = leaveFundService;
    }

    [HttpGet]
    [RequireGrant(Grants.RosterLeaveFundViewOwn, Grants.RosterLeaveFundViewAll)]
    public async Task<ActionResult<EmployeeLeaveFundsDto>> Get(Guid employeeId)
    {
        return Ok(await _leaveFundService.GetForEmployee(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.RosterLeaveFundViewAll), employeeId));
    }

    [HttpPost]
    [RequireGrant(Grants.RosterLeaveFundManage)]
    public async Task<ActionResult<LeaveFundDto>> ManualUpsert(Guid employeeId, [FromBody] LeaveFundManualUpsertRequest request)
    {
        return Ok(await _leaveFundService.ManualUpsert(this.CurrentOrganizationId(), this.CurrentUserId(), employeeId, request));
    }
}
