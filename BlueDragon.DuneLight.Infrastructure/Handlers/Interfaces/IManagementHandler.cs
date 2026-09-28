using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>
/// EKSKLUZIVNO mjesto u kodu s namjerno cross-tenant upitima (bez organization_id filtera) — vidi task zahtjev
/// "Cross-tenant access should be obvious in code" / "Do not modify existing tenant handlers to become globally
/// queryable". Koristi ga ISKLJUČIVO ManagementReadService, iza PlatformBearer JWT sheme (vidi
/// PlatformAuthenticationDefaults). Nijedan drugi
/// handler u projektu ne smije dobiti sličnu "svi organizationId" metodu — ako zatreba, ide ovdje.
/// </summary>
public interface IManagementHandler
{
    Task<ManagementOverviewDto> GetOverviewCounts();

    Task<(List<ManagementOrganizationListItemDto> Items, int TotalCount)> GetOrganizationsPaged(PagedRequest request);

    /// <summary>Vraća null ako Organization ne postoji — ManagementReadService baca NotFoundAppException.</summary>
    Task<ManagementOrganizationDetailDto> GetOrganizationDetail(Guid organizationId);

    Task<(List<ManagementUserListItemDto> Items, int TotalCount)> GetUsersPaged(PagedRequest request, Guid? organizationId);
}
