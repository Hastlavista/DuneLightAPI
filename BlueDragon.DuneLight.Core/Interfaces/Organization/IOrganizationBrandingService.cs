using System;
using System.IO;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

public interface IOrganizationBrandingService
{
    /// <summary>Javni branding za login screen — dostupan bez prijave. Baca NotFoundAppException za nepostojeći slug.</summary>
    Task<OrganizationBrandingResponse> GetPublicBranding(string organizationSlug);

    /// <summary>Puni branding za prijavljenog korisnika (settings).</summary>
    Task<OrganizationBrandingResponse> GetBranding(Guid organizationId);

    /// <summary>Ažurira obje boje odjednom (primary + secondary) — nema djelomičnog updatea.</summary>
    Task<OrganizationBrandingDto> UpdateColors(Guid organizationId, Guid userId, BrandingColorsUpdateRequest request);

    /// <summary>Vraća obje boje na platformski default (NULL).</summary>
    Task<OrganizationBrandingDto> ResetColors(Guid organizationId, Guid userId);

    /// <summary>Upload logotipa na disk, vraća javni URL.</summary>
    Task<BrandingUploadResponse> UploadLogo(Guid organizationId, Guid userId, Stream fileStream, string fileName, string contentType);

    /// <summary>Upload favicona na disk, vraća javni URL.</summary>
    Task<BrandingUploadResponse> UploadFavicon(Guid organizationId, Guid userId, Stream fileStream, string fileName, string contentType);

    /// <summary>Obriše logotip (disk + URL iz baze).</summary>
    Task<OrganizationBrandingDto> RemoveLogo(Guid organizationId, Guid userId);

    /// <summary>Obriše favicon (disk + URL iz baze).</summary>
    Task<OrganizationBrandingDto> RemoveFavicon(Guid organizationId, Guid userId);
}
