using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

public class ManagementReadService : IManagementReadService
{
    private readonly IManagementHandler _managementHandler;

    public ManagementReadService(IManagementHandler managementHandler)
    {
        _managementHandler = managementHandler;
    }

    public Task<ManagementOverviewDto> GetOverview()
    {
        return _managementHandler.GetOverviewCounts();
    }

    public async Task<PagedResult<ManagementOrganizationListItemDto>> GetOrganizations(PagedRequest request)
    {
        (var items, int totalCount) = await _managementHandler.GetOrganizationsPaged(request);
        return PagedResult<ManagementOrganizationListItemDto>.Create(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<ManagementOrganizationDetailDto> GetOrganizationDetail(Guid organizationId)
    {
        ManagementOrganizationDetailDto detail = await _managementHandler.GetOrganizationDetail(organizationId);
        if (detail == null)
            throw new NotFoundAppException("Organization", organizationId);

        return detail;
    }

    public async Task<PagedResult<ManagementUserListItemDto>> GetUsers(PagedRequest request, Guid? organizationId)
    {
        (var items, int totalCount) = await _managementHandler.GetUsersPaged(request, organizationId);
        return PagedResult<ManagementUserListItemDto>.Create(items, totalCount, request.Page, request.PageSize);
    }
}
