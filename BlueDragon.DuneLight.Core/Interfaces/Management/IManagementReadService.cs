using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Management;

/// <summary>Isključivo cross-tenant čitanje za DuneLight Platform Management (api/management/*, zaštićeno
/// PlatformBearer JWT shemom — vidi PlatformAuthenticationDefaults) — vidi IManagementHandler napomenu zašto je
/// namjerno odvojeno od svih tenant-scoped handlera/servisa.</summary>
public interface IManagementReadService
{
    Task<ManagementOverviewDto> GetOverview();

    Task<PagedResult<ManagementOrganizationListItemDto>> GetOrganizations(PagedRequest request);

    /// <summary>Baca NotFoundAppException("Organization", organizationId) ako ne postoji.</summary>
    Task<ManagementOrganizationDetailDto> GetOrganizationDetail(Guid organizationId);

    Task<PagedResult<ManagementUserListItemDto>> GetUsers(PagedRequest request, Guid? organizationId);
}
