using System;
using System.IO;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class OrganizationBrandingService : IOrganizationBrandingService
{
    private readonly IOrganizationBrandingHandler _organizationBrandingHandler;
    private readonly IOrganizationBrandingAuditLogHandler _auditLogHandler;
    private readonly IAuthHandler _authHandler;
    private readonly IBrandingFileStorage _brandingFileStorage;

    public OrganizationBrandingService(
        IOrganizationBrandingHandler organizationBrandingHandler,
        IOrganizationBrandingAuditLogHandler auditLogHandler,
        IAuthHandler authHandler,
        IBrandingFileStorage brandingFileStorage)
    {
        _organizationBrandingHandler = organizationBrandingHandler;
        _auditLogHandler = auditLogHandler;
        _authHandler = authHandler;
        _brandingFileStorage = brandingFileStorage;
    }

    public async Task<OrganizationBrandingResponse> GetPublicBranding(string organizationSlug)
    {
        Organization organization = await _authHandler.GetOrganizationBySlug(organizationSlug);
        if (organization == null)
            throw new NotFoundAppException("Organization", organizationSlug);

        return ToResponse(organization);
    }

    public async Task<OrganizationBrandingResponse> GetBranding(Guid organizationId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);
        return ToResponse(organization);
    }

    public async Task<OrganizationBrandingDto> UpdateColors(Guid organizationId, Guid userId, BrandingColorsUpdateRequest request)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldPrimary = organization.PrimaryColor;
        string oldSecondary = organization.SecondaryColor;
        string oldSurface = organization.SurfaceColor;
        string newPrimary = NormalizeHex(request.PrimaryColor);
        string newSecondary = NormalizeHex(request.SecondaryColor);
        string newSurface = NormalizeHex(request.SurfaceColor);

        organization.PrimaryColor = newPrimary;
        organization.SecondaryColor = newSecondary;
        organization.SurfaceColor = newSurface;
        await _organizationBrandingHandler.Update(organization);

        await AddAuditEntry(organizationId, userId, "ColorsUpdated", $"{oldPrimary}/{oldSecondary}/{oldSurface}", $"{newPrimary}/{newSecondary}/{newSurface}");

        return ToDto(organization);
    }

    public async Task<OrganizationBrandingDto> ResetColors(Guid organizationId, Guid userId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldPrimary = organization.PrimaryColor;
        string oldSecondary = organization.SecondaryColor;
        string oldSurface = organization.SurfaceColor;

        organization.PrimaryColor = null;
        organization.SecondaryColor = null;
        organization.SurfaceColor = null;
        await _organizationBrandingHandler.Update(organization);

        await AddAuditEntry(organizationId, userId, "ColorsReset", $"{oldPrimary}/{oldSecondary}/{oldSurface}", null);

        return ToDto(organization);
    }

    public async Task<BrandingUploadResponse> UploadLogo(Guid organizationId, Guid userId, Stream fileStream, string fileName, string contentType)
    {
        string newUrl = await UploadFile(organizationId, fileStream, fileName, contentType, "logo",
            (organization, url) => organization.Logo = url,
            organization => organization.Logo,
            userId, "LogoUploaded");

        return new BrandingUploadResponse { Url = newUrl };
    }

    public async Task<BrandingUploadResponse> UploadFavicon(Guid organizationId, Guid userId, Stream fileStream, string fileName, string contentType)
    {
        string newUrl = await UploadFile(organizationId, fileStream, fileName, contentType, "favicon",
            (organization, url) => organization.Favicon = url,
            organization => organization.Favicon,
            userId, "FaviconUploaded");

        return new BrandingUploadResponse { Url = newUrl };
    }

    public async Task<OrganizationBrandingDto> RemoveLogo(Guid organizationId, Guid userId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldUrl = organization.Logo;
        organization.Logo = null;
        await _organizationBrandingHandler.Update(organization);
        await _brandingFileStorage.DeleteAsync(oldUrl);

        await AddAuditEntry(organizationId, userId, "LogoDeleted", oldUrl, null);

        return ToDto(organization);
    }

    public async Task<OrganizationBrandingDto> RemoveFavicon(Guid organizationId, Guid userId)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);

        string oldUrl = organization.Favicon;
        organization.Favicon = null;
        await _organizationBrandingHandler.Update(organization);
        await _brandingFileStorage.DeleteAsync(oldUrl);

        await AddAuditEntry(organizationId, userId, "FaviconDeleted", oldUrl, null);

        return ToDto(organization);
    }

    /// <summary>
    /// Zajednički upload flow za logo/favicon (spec redoslijed): spremi novu datoteku pod novim GUID imenom,
    /// zatim ažuriraj DB. Tek nakon uspješnog DB updatea briše se stara datoteka (best-effort, vidi
    /// BrandingFileStorage.DeleteAsync). Ako DB update ne uspije, briše se upravo uploadana nova datoteka i
    /// iznimka se propagira dalje.
    /// </summary>
    private async Task<string> UploadFile(
        Guid organizationId,
        Stream fileStream,
        string fileName,
        string contentType,
        string filePrefix,
        Action<Organization, string> setUrl,
        Func<Organization, string> getUrl,
        Guid userId,
        string changeType)
    {
        Organization organization = await GetOrganizationOrThrow(organizationId);
        string oldUrl = getUrl(organization);

        string newUrl = await _brandingFileStorage.SaveAsync(fileStream, fileName, contentType, organization.Slug, filePrefix);

        try
        {
            setUrl(organization, newUrl);
            await _organizationBrandingHandler.Update(organization);
        }
        catch
        {
            await _brandingFileStorage.DeleteAsync(newUrl);
            throw;
        }

        await _brandingFileStorage.DeleteAsync(oldUrl);
        await AddAuditEntry(organizationId, userId, changeType, oldUrl, newUrl);

        return newUrl;
    }

    private async Task AddAuditEntry(Guid organizationId, Guid userId, string changeType, string oldValue, string newValue)
    {
        await _auditLogHandler.Add(new OrganizationBrandingAuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ChangeType = changeType,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
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

        string trimmed = value.Trim().ToUpperInvariant();
        return trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed : $"#{trimmed}";
    }

    private static OrganizationBrandingDto ToDto(Organization organization)
    {
        return new OrganizationBrandingDto
        {
            Logo = organization.Logo,
            Favicon = organization.Favicon,
            PrimaryColor = organization.PrimaryColor,
            SecondaryColor = organization.SecondaryColor,
            SurfaceColor = organization.SurfaceColor
        };
    }

    private static OrganizationBrandingResponse ToResponse(Organization organization)
    {
        return new OrganizationBrandingResponse
        {
            Logo = organization.Logo,
            Favicon = organization.Favicon,
            PrimaryColor = organization.PrimaryColor,
            SecondaryColor = organization.SecondaryColor,
            SurfaceColor = organization.SurfaceColor,
            OrganizationName = organization.Name,
            OrganizationSlug = organization.Slug
        };
    }
}
