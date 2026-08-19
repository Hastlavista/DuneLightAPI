using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Groups;

[ApiController]
[Route("api/groups/appointments/{appointmentId:guid}/attendance")]
[Produces("application/json")]
public class GroupAttendanceController : ControllerBase
{
    private readonly IGroupAttendanceService _attendanceService;

    public GroupAttendanceController(IGroupAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    /// <summary>Expected = aktivni članovi grupe bez zabilježene prisutnosti (kandidati). Recorded = već
    /// zabilježeni retci (uklj. goste izvan popisa članova).</summary>
    [HttpGet]
    [RequireGrant(Grants.GroupsAttendanceView)]
    public async Task<ActionResult<GroupAttendanceListDto>> GetAttendance(Guid appointmentId)
    {
        return Ok(await _attendanceService.GetAttendance(this.CurrentOrganizationId(), appointmentId));
    }

    /// <summary>Čekira/poništava prisutnost jednog klijenta (član ili gost izvan popisa). Member smije samo na
    /// svom terminu. Poništenje SessionPackage prisutnosti automatski vraća skinuti ulazak.</summary>
    [HttpPost]
    [RequireGrant(Grants.GroupsAttendanceOwn, Grants.GroupsAttendanceAll)]
    public async Task<ActionResult<GroupAttendanceListDto>> SetAttendance(Guid appointmentId, [FromBody] SetGroupAttendanceRequest request)
    {
        return Ok(await _attendanceService.SetAttendance(
            this.CurrentOrganizationId(), this.CurrentUserId(), this.HasGrant(Grants.GroupsAttendanceAll), appointmentId, request));
    }
}
