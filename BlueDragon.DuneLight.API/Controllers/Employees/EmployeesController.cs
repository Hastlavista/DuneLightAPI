using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Employees;

[ApiController]
[Route("api/employees")]
[Produces("application/json")]
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    /// <summary>Ograničeni pogled za kolege — bez osjetljivih polja.</summary>
    [HttpGet("directory")]
    [Authorize(Roles = "Admin,Member,Reception")]
    public async Task<ActionResult<PagedResult<EmployeeDirectoryDto>>> GetDirectory([FromQuery] PagedRequest request)
    {
        return Ok(await _employeeService.GetDirectory(this.CurrentOrganizationId(), request));
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PagedResult<EmployeeDto>>> GetPaged(
        [FromQuery] PagedRequest request, [FromQuery] Guid? locationId, [FromQuery] Guid? engagementTypeId, [FromQuery] UserRole? role)
    {
        return Ok(await _employeeService.GetPaged(this.CurrentOrganizationId(), request, locationId, engagementTypeId, role));
    }

    /// <summary>Zaposlenik povezan s prijavljenim korisnikom — "tko sam ja" (za "moji termini"/"moji klijenti"/"moje smjene" na frontendu).</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<EmployeeMeDto>> GetMe()
    {
        return Ok(await _employeeService.GetMe(this.CurrentOrganizationId(), this.CurrentUserId()));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> GetById(Guid id)
    {
        return Ok(await _employeeService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> Create([FromBody] EmployeeCreateRequest request)
    {
        EmployeeDto created = await _employeeService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Kreira korisnički račun i zaposlenika u jednom zahtjevu/jednoj transakciji (za slučaj kad zaposlenik još nema login).</summary>
    [HttpPost("with-login")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeWithLoginCreateResponse>> CreateWithLogin([FromBody] EmployeeWithLoginCreateRequest request)
    {
        EmployeeWithLoginCreateResponse created = await _employeeService.CreateWithLogin(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.EmployeeId }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> Update(Guid id, [FromBody] EmployeeUpdateRequest request)
    {
        return Ok(await _employeeService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> Activate(Guid id)
    {
        return Ok(await _employeeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    [HttpPatch("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> Deactivate(Guid id)
    {
        return Ok(await _employeeService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpPatch("{id:guid}/role")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EmployeeDto>> UpdateRole(Guid id, [FromBody] UpdateEmployeeRoleRequest request)
    {
        return Ok(await _employeeService.UpdateRole(this.CurrentOrganizationId(), this.CurrentUserId(), id, request.Role));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _employeeService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }
}
