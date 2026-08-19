using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Employees;

[ApiController]
[Route("api/employees/engagement-types")]
[Produces("application/json")]
public class EngagementTypesController : ControllerBase
{
    private readonly IEngagementTypeService _engagementTypeService;

    public EngagementTypesController(IEngagementTypeService engagementTypeService)
    {
        _engagementTypeService = engagementTypeService;
    }

    [HttpGet]
    [RequireGrant(Grants.EmployeesEngagementTypesView)]
    public async Task<ActionResult<PagedResult<EngagementTypeDto>>> GetPaged([FromQuery] PagedRequest request)
    {
        return Ok(await _engagementTypeService.GetPaged(this.CurrentOrganizationId(), request));
    }

    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.EmployeesEngagementTypesView)]
    public async Task<ActionResult<EngagementTypeDto>> GetById(Guid id)
    {
        return Ok(await _engagementTypeService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.EmployeesEngagementTypesManage)]
    public async Task<ActionResult<EngagementTypeDto>> Create([FromBody] EngagementTypeCreateRequest request)
    {
        EngagementTypeDto created = await _engagementTypeService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.EmployeesEngagementTypesManage)]
    public async Task<ActionResult<EngagementTypeDto>> Update(Guid id, [FromBody] EngagementTypeUpdateRequest request)
    {
        return Ok(await _engagementTypeService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.EmployeesEngagementTypesManage)]
    public async Task<ActionResult<EngagementTypeDto>> Activate(Guid id)
    {
        return Ok(await _engagementTypeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.EmployeesEngagementTypesManage)]
    public async Task<ActionResult<EngagementTypeDto>> Deactivate(Guid id)
    {
        return Ok(await _engagementTypeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpDelete("{id:guid}")]
    [RequireGrant(Grants.EmployeesEngagementTypesManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _engagementTypeService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}