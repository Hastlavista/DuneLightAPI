using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Permissions;

/// <summary>Upravljanje GrantGroup-ama — Owner-only (vidi RequireOwnerAttribute), dok se ne pokaže potreba da i ne-Owner Admin upravlja dozvolama.</summary>
[ApiController]
[Route("api/permissions/grant-groups")]
[Produces("application/json")]
[RequireOwner]
public class GrantGroupsController : ControllerBase
{
    private readonly IGrantGroupService _grantGroupService;
    private readonly ICapabilityReadService _capabilityReadService;
    private readonly IGrantGroupCapabilityAuthoringService _authoringService;

    public GrantGroupsController(IGrantGroupService grantGroupService, ICapabilityReadService capabilityReadService, IGrantGroupCapabilityAuthoringService authoringService)
    {
        _grantGroupService = grantGroupService;
        _capabilityReadService = capabilityReadService;
        _authoringService = authoringService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GrantGroupDto>>> GetAll()
    {
        return Ok(await _grantGroupService.GetAll(this.CurrentOrganizationId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GrantGroupDto>> GetById(Guid id)
    {
        return Ok(await _grantGroupService.GetById(this.CurrentOrganizationId(), id));
    }

    [HttpPost]
    public async Task<ActionResult<GrantGroupDto>> Create([FromBody] GrantGroupCreateRequest request)
    {
        GrantGroupDto created = await _grantGroupService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GrantGroupDto>> Update(Guid id, [FromBody] GrantGroupUpdateRequest request)
    {
        return Ok(await _grantGroupService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _grantGroupService.Delete(this.CurrentOrganizationId(), id);
        return NoContent();
    }

    [HttpGet("assignments/{userId:guid}")]
    public async Task<ActionResult<List<Guid>>> GetAssignments(Guid userId)
    {
        return Ok(await _grantGroupService.GetAssignedGrantGroupIds(this.CurrentOrganizationId(), userId));
    }

    /// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika.</summary>
    [HttpPut("assignments/{userId:guid}")]
    public async Task<IActionResult> SetAssignments(Guid userId, [FromBody] AssignUserGrantGroupsRequest request)
    {
        await _grantGroupService.SetUserGrantGroups(this.CurrentOrganizationId(), userId, request);
        return NoContent();
    }

    /// <summary>FAZA 1 Part R — je li ova GrantGroup nastala iz predloška (i s kojim capability odabirima),
    /// isključivo iz stabilnih snapshot metapodataka (vidi GrantGroupCapabilitySnapshot). Prazan rezultat
    /// (HasSnapshotMetadata=false) znači da grupa nema capability provenance (custom, ili predviđena prije ove faze).</summary>
    [HttpGet("{id:guid}/template-match")]
    public async Task<ActionResult<GrantGroupTemplateMatchDto>> GetTemplateMatch(Guid id)
    {
        return Ok(await _capabilityReadService.GetGrantGroupTemplateMatch(this.CurrentOrganizationId(), id));
    }

    /// <summary>FAZA 2 Part D — capability-aware create. Klijent šalje capability odabire + legitimne ručne
    /// grantove; backend materijalizira i piše autoritativni raw grant skup (vidi IGrantGroupCapabilityAuthoringService).</summary>
    [HttpPost("capability-based")]
    public async Task<ActionResult<GrantGroupAuthoringDto>> CreateCapabilityBased([FromBody] GrantGroupCapabilityWriteRequest request)
    {
        GrantGroupAuthoringDto created = await _authoringService.Create(this.CurrentOrganizationId(), this.CurrentUserId(), request);
        return CreatedAtAction(nameof(GetAuthoringState), new { id = created.GrantGroup.Id }, created);
    }

    /// <summary>FAZA 2 Part E — capability-aware ordinary edit. Eksplicitni full-state save iz role-editora:
    /// ManualGrantKeys predstavlja Ownerov NOVI željeni ručni skup (ne automatski očuvan stari), vidi
    /// GrantGroupHandler.ApplyCapabilitySelections napomenu zašto se razlikuje od ApplyTemplate.</summary>
    [HttpPut("{id:guid}/capability-based")]
    public async Task<ActionResult<GrantGroupAuthoringDto>> UpdateCapabilityBased(Guid id, [FromBody] GrantGroupCapabilityWriteRequest request)
    {
        return Ok(await _authoringService.Update(this.CurrentOrganizationId(), this.CurrentUserId(), id, request));
    }

    /// <summary>FAZA 2 Part I/J — trenutno autoritativno authoring stanje (capability odabiri, ručni grantovi,
    /// template provenance, customized flag). Za legacy/drifted grupu (bez snapshot metapodataka) NIKAD ne
    /// izmišlja capability odabire niti nešto piše u bazu — vidi GrantGroupAuthoringDto.HasCapabilityMetadata.</summary>
    [HttpGet("{id:guid}/authoring-state")]
    public async Task<ActionResult<GrantGroupAuthoringDto>> GetAuthoringState(Guid id)
    {
        return Ok(await _authoringService.GetAuthoringState(this.CurrentOrganizationId(), id));
    }
}