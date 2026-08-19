using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Groups;

[ApiController]
[Route("api/groups")]
[Produces("application/json")]
public class GroupsController : ControllerBase
{
    private readonly IGroupService _groupService;

    public GroupsController(IGroupService groupService)
    {
        _groupService = groupService;
    }

    [HttpGet]
    [RequireGrant(Grants.GroupsView)]
    public async Task<ActionResult<List<GroupDto>>> GetAll([FromQuery] bool? isActive)
    {
        return Ok(await _groupService.GetAll(this.CurrentOrganizationId(), isActive));
    }

    /// <summary>Detalj grupe — članovi, slotovi, nadolazeći i protekli termini.</summary>
    [HttpGet("{id:guid}")]
    [RequireGrant(Grants.GroupsView)]
    public async Task<ActionResult<GroupDetailDto>> GetById(Guid id)
    {
        return Ok(await _groupService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> Create([FromBody] GroupCreateRequest request)
    {
        GroupDto created = await _groupService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> Update(Guid id, [FromBody] GroupUpdateRequest request)
    {
        return Ok(await _groupService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpPatch("{id:guid}/activate")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> Activate(Guid id)
    {
        return Ok(await _groupService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, true));
    }

    /// <summary>Ne dira već generirane termine — zaustavlja samo buduće generiranje.</summary>
    [HttpPatch("{id:guid}/deactivate")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> Deactivate(Guid id)
    {
        return Ok(await _groupService.SetActive(this.CurrentOrganizationId(), this.CurrentUserId(), id, false));
    }

    [HttpPost("{id:guid}/slots")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> AddSlot(Guid id, [FromBody] GroupSlotCreateRequest request)
    {
        return Ok(await _groupService.AddSlot(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Izmjena dana/vremena slota ne dira već generirane termine — utječe samo na sljedeća generiranja.</summary>
    [HttpPut("{id:guid}/slots/{slotId:guid}")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> UpdateSlot(Guid id, Guid slotId, [FromBody] GroupSlotUpdateRequest request)
    {
        return Ok(await _groupService.UpdateSlot(this.CurrentOrganizationId(), this.CurrentUserId(), id, slotId, request));
    }

    /// <summary>Deaktivira slot (nema tvrdog brisanja) — blokirano ako je posljednji aktivan slot grupe.</summary>
    [HttpDelete("{id:guid}/slots/{slotId:guid}")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> RemoveSlot(Guid id, Guid slotId)
    {
        return Ok(await _groupService.RemoveSlot(this.CurrentOrganizationId(), this.CurrentUserId(), id, slotId));
    }

    /// <summary>Kapacitet je upozorenje, ne zabrana — dodavanje preko kapaciteta se dopušta uz Warnings u odgovoru.</summary>
    [HttpPost("{id:guid}/members")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> AddMember(Guid id, [FromBody] GroupMemberAddRequest request)
    {
        return Ok(await _groupService.AddMember(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>Deaktivira članstvo (napuštanje grupe) — ne dira povijesne termine/prisutnost.</summary>
    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GroupDto>> RemoveMember(Guid id, Guid memberId)
    {
        return Ok(await _groupService.RemoveMember(this.CurrentOrganizationId(), this.CurrentUserId(), id, memberId));
    }

    /// <summary>Idempotentno generira grupne termine za zadani raspon — GroupId=null generira za sve aktivne grupe.
    /// Ponovno pokretanje za isti raspon ne stvara duplikate.</summary>
    [HttpPost("generate-appointments")]
    [RequireGrant(Grants.GroupsManage)]
    public async Task<ActionResult<GenerateGroupAppointmentsResult>> GenerateAppointments([FromBody] GenerateGroupAppointmentsRequest request)
    {
        return Ok(await _groupService.GenerateAppointments(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }
}
