using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class EngagementTypeService : IEngagementTypeService
{
    private readonly IEngagementTypeHandler _engagementTypeHandler;

    public EngagementTypeService(IEngagementTypeHandler engagementTypeHandler)
    {
        _engagementTypeHandler = engagementTypeHandler;
    }

    public async Task<PagedResult<EngagementTypeDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<EngagementType> items, int totalCount) = await _engagementTypeHandler.GetPaged(organizationId, request);
        return PagedResult<EngagementTypeDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<EngagementTypeDto> GetById(Guid organizationId, Guid id)
    {
        EngagementType engagementType = await _engagementTypeHandler.GetById(organizationId, id);
        if (engagementType == null)
            throw new NotFoundAppException("EngagementType", id);

        return ToDto(engagementType);
    }

    public async Task<EngagementTypeDto> Create(Guid organizationId, Guid userId, EngagementTypeCreateRequest request)
    {
        await EnsureNameIsUnique(organizationId, request.Name, excludeId: null);

        EngagementType engagementType = new EngagementType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _engagementTypeHandler.Add(engagementType);
        return ToDto(engagementType);
    }

    public async Task<EngagementTypeDto> Update(Guid organizationId, Guid userId, Guid id, EngagementTypeUpdateRequest request)
    {
        EngagementType engagementType = await _engagementTypeHandler.GetById(organizationId, id);
        if (engagementType == null)
            throw new NotFoundAppException("EngagementType", id);

        await EnsureNameIsUnique(organizationId, request.Name, excludeId: id);

        engagementType.Name = request.Name;
        engagementType.SortOrder = request.SortOrder;
        engagementType.UpdatedAt = DateTimeOffset.UtcNow;
        engagementType.UpdatedBy = userId;

        await _engagementTypeHandler.Update(engagementType);
        return ToDto(engagementType);
    }

    public async Task<EngagementTypeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        EngagementType engagementType = await _engagementTypeHandler.GetById(organizationId, id);
        if (engagementType == null)
            throw new NotFoundAppException("EngagementType", id);

        if (isActive)
            await EnsureNameIsUnique(organizationId, engagementType.Name, excludeId: id);

        engagementType.IsActive = isActive;
        engagementType.UpdatedAt = DateTimeOffset.UtcNow;
        engagementType.UpdatedBy = userId;

        await _engagementTypeHandler.Update(engagementType);
        return ToDto(engagementType);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        EngagementType engagementType = await _engagementTypeHandler.GetById(organizationId, id);
        if (engagementType == null)
            throw new NotFoundAppException("EngagementType", id);

        bool isReferenced = await _engagementTypeHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Vrstu angažmana koristi barem jedan zaposlenik i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _engagementTypeHandler.Delete(engagementType);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _engagementTypeHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna vrsta angažmana s nazivom '{name}' već postoji.");
    }

    private static EngagementTypeDto ToDto(EngagementType engagementType)
    {
        return new EngagementTypeDto
        {
            Id = engagementType.Id.GetValueOrDefault(),
            Name = engagementType.Name,
            IsActive = engagementType.IsActive,
            SortOrder = engagementType.SortOrder,
            CreatedAt = engagementType.CreatedAt,
            CreatedBy = engagementType.CreatedBy,
            UpdatedAt = engagementType.UpdatedAt,
            UpdatedBy = engagementType.UpdatedBy
        };
    }
}
