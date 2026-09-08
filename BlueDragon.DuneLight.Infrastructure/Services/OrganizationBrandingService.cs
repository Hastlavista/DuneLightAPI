using System;
using System.IO;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class OrganizationBrandingService : IOrganizationBrandingService
{
    private readonly IOrganizationBrandingHandler _organizationBrandingHandler;
    private readonly IAuthHandler _authHandler;
    private readonly IBrandingFileStorage _brandingFileStorage;

    public OrganizationBrandingService(
        IOrganizationBrandingHandler organizationBrandingHandler,
        IAuthHandler authHandler,
        IBrandingFileStorage brandingFileStorage)
    {
        _organizationBrandingHandler = organizationBrandingHandler;
        _authHandler = authHandler;
        _brandingFileStorage = brandingFileStorage;
    }

    public async Task<OrganizationBrandingDto> GetPublicBranding(string organizationSlug)
    {
        Organization organization = await _authHandler.GetOrganizationBySlug(organizationSlug);
        if (organization == null)
            throw new NotFoundAppException("Organization", organizationSlug);

        return ToDto(organization);
    }

    public async Task<OrganizationBrandingResponse> GetBranding(Guid organizationId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        return new OrganizationBrandingResponse
        {
            Logo = organization.Logo,
            Favicon = organization.Favicon,
            PrimaryColor = organization.PrimaryColor,
            SecondaryColor = organization.SecondaryColor,
            OrganizationName = organization.Name,
            OrganizationSlug = organization.Slug
        };
    }

    public async Task<OrganizationBrandingDto> UpdateColors(Guid organizationId, BrandingColorsUpdateRequest request)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        organization.PrimaryColor = NormalizeHex(request.PrimaryColor);
        organization.SecondaryColor = NormalizeHex(request.SecondaryColor);

        await _organizationBrandingHandler.Update(organization);
        return ToDto(organization);
    }

    public async Task<BrandingUploadResponse> UploadLogo(Guid organizationId, Stream fileStream, string fileName)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldUrl = organization.Logo;
        string newUrl = await _brandingFileStorage.SaveAsync(fileStream, fileName, organization.Slug, "logo");
        await _brandingFileStorage.DeleteAsync(oldUrl);

        organization.Logo = newUrl;
        await _organizationBrandingHandler.Update(organization);

        return new BrandingUploadResponse { Url = newUrl };
    }

    public async Task<BrandingUploadResponse> UploadFavicon(Guid organizationId, Stream fileStream, string fileName)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldUrl = organization.Favicon;
        string newUrl = await _brandingFileStorage.SaveAsync(fileStream, fileName, organization.Slug, "favicon");
        await _brandingFileStorage.DeleteAsync(oldUrl);

        organization.Favicon = newUrl;
        await _organizationBrandingHandler.Update(organization);

        return new BrandingUploadResponse { Url = newUrl };
    }

    public async Task<OrganizationBrandingDto> RemoveLogo(Guid organizationId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        await _brandingFileStorage.DeleteAsync(organization.Logo);
        organization.Logo = null;
        await _organizationBrandingHandler.Update(organization);

        return ToDto(organization);
    }

    public async Task<OrganizationBrandingDto> RemoveFavicon(Guid organizationId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        await _brandingFileStorage.DeleteAsync(organization.Favicon);
        organization.Favicon = null;
        await _organizationBrandingHandler.Update(organization);

        return ToDto(organization);
    }

    private async Task<Organization> GetOrganizationOrThrow(Guid organizationId)
    {
        Organization organization = await _organizationBrandingHandler.GetById(organizationId);
        if (organization == null)
            throw new NotFoundAppException("Organization", organizationId);
        return organization;
    }

    private static string NormalizeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : $"#{trimmed}";
    }

    private static OrganizationBrandingDto ToDto(Organization organization)
    {
        return new OrganizationBrandingDto
        {
            Logo = organization.Logo,
            Favicon = organization.Favicon,
            PrimaryColor = organization.PrimaryColor,
            SecondaryColor = organization.SecondaryColor
        };
    }
}
