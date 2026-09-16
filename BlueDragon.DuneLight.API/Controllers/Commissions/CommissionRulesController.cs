using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Commissions;

[ApiController]
[Route("api/commissions/rules")]
[Produces("application/json")]
public class CommissionRulesController : ControllerBase
{
    private readonly ICommissionRuleService _commissionRuleService;

    public CommissionRulesController(ICommissionRuleService commissionRuleService)
    {
        _commissionRuleService = commissionRuleService;
    }

    [HttpGet]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<List<CommissionRuleDto>>> GetList([FromQuery] CommissionRuleQuery query)
    {
        return Ok(await _commissionRuleService.GetList(this.CurrentOrganizationId(), query));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionRuleDto>> GetById(Guid id)
    {
        return Ok(await _commissionRuleService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionRuleDto>> Create([FromBody] CommissionRuleCreateRequest request)
    {
        CommissionRuleDto created = await _commissionRuleService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionRuleDto>> Update(Guid id, [FromBody] CommissionRuleUpdateRequest request)
    {
        return Ok(await _commissionRuleService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionRuleDto>> Activate(Guid id)
    {
        return Ok(await _commissionRuleService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<ActionResult<CommissionRuleDto>> Deactivate(Guid id)
    {
        return Ok(await _commissionRuleService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.CommissionsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _commissionRuleService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
