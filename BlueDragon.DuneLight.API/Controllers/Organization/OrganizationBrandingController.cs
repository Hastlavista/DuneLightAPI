using System;
using System.IO;
using System.Threading.Tasks;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.API.Extensions;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Organization;

/// <summary>
/// Vizualni identitet (branding) organizacije — boje, upload/brisanje logotipa i favicona. Sve upravljačke
/// akcije zahtijevaju Grant <see cref="Grants.OrganizationBrandingManage"/> (Owner ga uvijek ima — vidi
/// GrantContext.Has — a Admin grupa ga dobiva po defaultu, vidi DefaultGrantGroups.AdminGrants).
/// Javni GET za login screen (<see cref="GetPublicBranding"/>) ne zahtijeva prijavu.
/// </summary>
[ApiController]
[Route("api/organization/branding")]
[Produces("application/json")]
public class OrganizationBrandingController : ControllerBase
{
    private readonly IOrganizationBrandingService _organizationBrandingService;

    public OrganizationBrandingController(IOrganizationBrandingService organizationBrandingService)
    {
        _organizationBrandingService = organizationBrandingService;
    }

    /// <summary>Javni branding za login screen — bez prijave, po slugu organizacije.</summary>
    [HttpGet("public/{organizationSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<OrganizationBrandingResponse>> GetPublicBranding(string organizationSlug)
    {
        return Ok(await _organizationBrandingService.GetPublicBranding(organizationSlug));
    }

    [HttpGet]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    public async Task<ActionResult<OrganizationBrandingResponse>> GetBranding()
    {
        return Ok(await _organizationBrandingService.GetBranding(this.CurrentOrganizationId()));
    }

    [HttpPut("colors")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    public async Task<ActionResult<OrganizationBrandingDto>> UpdateColors([FromBody] BrandingColorsUpdateRequest request)
    {
        return Ok(await _organizationBrandingService.UpdateColors(this.CurrentOrganizationId(), this.CurrentUserId(), request));
    }

    /// <summary>Vraća obje boje na platformski default (NULL) — čist "reset" postupak, odvojen od PUT colors.</summary>
    [HttpDelete("colors")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    public async Task<ActionResult<OrganizationBrandingDto>> ResetColors()
    {
        return Ok(await _organizationBrandingService.ResetColors(this.CurrentOrganizationId(), this.CurrentUserId()));
    }

    [HttpPost("upload/logo")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<BrandingUploadResponse>> UploadLogo(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ValidationAppException("Nije poslana datoteka za upload.");

        await using Stream stream = file.OpenReadStream();
        BrandingUploadResponse result = await _organizationBrandingService.UploadLogo(this.CurrentOrganizationId(), this.CurrentUserId(), stream, file.FileName, file.ContentType);
        return Ok(result);
    }

    [HttpPost("upload/favicon")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<BrandingUploadResponse>> UploadFavicon(IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ValidationAppException("Nije poslana datoteka za upload.");

        await using Stream stream = file.OpenReadStream();
        BrandingUploadResponse result = await _organizationBrandingService.UploadFavicon(this.CurrentOrganizationId(), this.CurrentUserId(), stream, file.FileName, file.ContentType);
        return Ok(result);
    }

    [HttpDelete("logo")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    public async Task<ActionResult<OrganizationBrandingDto>> RemoveLogo()
    {
        return Ok(await _organizationBrandingService.RemoveLogo(this.CurrentOrganizationId(), this.CurrentUserId()));
    }

    [HttpDelete("favicon")]
    [RequireGrant(Grants.OrganizationBrandingManage)]
    public async Task<ActionResult<OrganizationBrandingDto>> RemoveFavicon()
    {
        return Ok(await _organizationBrandingService.RemoveFavicon(this.CurrentOrganizationId(), this.CurrentUserId()));
    }
}
