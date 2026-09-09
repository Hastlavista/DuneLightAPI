using System.IO;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Controllers.Organization;

/// <summary>
/// Javno serviranje branding datoteka (logo/favicon) za login screen i ostale javne dijelove interfejsa.
/// Datoteke se čitaju s diska i serviraju pod pravilnim Content-Typeom. Autentifikacija nije potrebna
/// (logotip/favicon nisu osjetljivi podaci — vidljivi svima).
/// </summary>
[ApiController]
[Route("uploads/branding")]
[AllowAnonymous]
public class BrandingFileController : ControllerBase
{
    private readonly IBrandingFileStorage _brandingFileStorage;

    public BrandingFileController(IBrandingFileStorage brandingFileStorage)
    {
        _brandingFileStorage = brandingFileStorage;
    }

    [HttpGet("{organizationSlug}/{fileName}")]
    public IActionResult Get(string organizationSlug, string fileName)
    {
        string publicUrl = $"/uploads/branding/{organizationSlug}/{fileName}";
        string physicalPath = _brandingFileStorage.GetPhysicalPath(publicUrl);

        if (string.IsNullOrEmpty(physicalPath) || !System.IO.File.Exists(physicalPath))
            return NotFound();

        Stream stream = _brandingFileStorage.OpenRead(publicUrl, out string contentType);

        // Nosniff sprječava browser da MIME-sniffa sadržaj drugačije od deklariranog Content-Typea (obrana u
        // dubinu uz magic-byte validaciju na uploadu). Dugi immutable cache je siguran jer svaki upload
        // dobiva novi GUID URL — promjena logotipa/favicona uvijek znači novu putanju, ne prepisivanje stare.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";

        return File(stream, contentType);
    }
}
