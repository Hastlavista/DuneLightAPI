using System;
using System.IO;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;

namespace BlueDragon.DuneLight.Core.Interfaces.Organization;

public interface IOrganizationBrandingService
{
    /// <summary>Javni branding za login screen — dostupan bez prijave.</summary>
    Task<OrganizationBrandingDto> GetPublicBranding(string organizationSlug);

    /// <summary>Puni branding za prijavljenog korisnika (settings).</summary>
    Task<OrganizationBrandingResponse> GetBranding(Guid organizationId);

    /// <summary>Ažurira samo boje (primary/secondary).</summary>
    Task<OrganizationBrandingDto> UpdateColors(Guid organizationId, BrandingColorsUpdateRequest request);

    /// <summary>Upload logotipa na disk, vraća javni URL.</summary>
    Task<BrandingUploadResponse> UploadLogo(Guid organizationId, Stream fileStream, string fileName);

    /// <summary>Upload favicona na disk, vraća javni URL.</summary>
    Task<BrandingUploadResponse> UploadFavicon(Guid organizationId, Stream fileStream, string fileName);

    /// <summary>Obriše logotip (disk + URL iz baze).</summary>
    Task<OrganizationBrandingDto> RemoveLogo(Guid organizationId);

    /// <summary>Obriše favicon (disk + URL iz baze).</summary>
    Task<OrganizationBrandingDto> RemoveFavicon(Guid organizationId);
}
