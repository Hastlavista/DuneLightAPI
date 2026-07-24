using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class RosterTypeService : IRosterTypeService
{
    private readonly IRosterTypeHandler _rosterTypeHandler;

    public RosterTypeService(IRosterTypeHandler rosterTypeHandler)
    {
        _rosterTypeHandler = rosterTypeHandler;
    }

    public async Task<PagedResult<RosterTypeDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<RosterType> items, int totalCount) = await _rosterTypeHandler.GetPaged(organizationId, request);
        return PagedResult<RosterTypeDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<RosterTypeDto> GetById(Guid organizationId, Guid id)
    {
        RosterType rosterType = await _rosterTypeHandler.GetById(organizationId, id);
        if (rosterType == null)
            throw new NotFoundAppException("RosterType", id);

        return ToDto(rosterType);
    }

    public async Task<RosterTypeDto> Create(Guid organizationId, Guid userId, RosterTypeCreateRequest request)
    {
        await EnsureNameIsUnique(organizationId, request.Name, excludeId: null);

        RosterType rosterType = new RosterType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            ColorHex = request.ColorHex,
            CountsAsWork = request.CountsAsWork,
            IsAbsence = request.IsAbsence,
            RequiresTime = request.RequiresTime,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _rosterTypeHandler.Add(rosterType);
        return ToDto(rosterType);
    }

    public async Task<RosterTypeDto> Update(Guid organizationId, Guid userId, Guid id, RosterTypeUpdateRequest request)
    {
        RosterType rosterType = await _rosterTypeHandler.GetById(organizationId, id);
        if (rosterType == null)
            throw new NotFoundAppException("RosterType", id);

        await EnsureNameIsUnique(organizationId, request.Name, excludeId: id);

        rosterType.Name = request.Name;
        rosterType.ColorHex = request.ColorHex;
        rosterType.CountsAsWork = request.CountsAsWork;
        rosterType.IsAbsence = request.IsAbsence;
        rosterType.RequiresTime = request.RequiresTime;
        rosterType.SortOrder = request.SortOrder;
        rosterType.UpdatedAt = DateTimeOffset.UtcNow;
        rosterType.UpdatedBy = userId;

        await _rosterTypeHandler.Update(rosterType);
        return ToDto(rosterType);
    }

    public async Task<RosterTypeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        RosterType rosterType = await _rosterTypeHandler.GetById(organizationId, id);
        if (rosterType == null)
            throw new NotFoundAppException("RosterType", id);

        if (isActive)
            await EnsureNameIsUnique(organizationId, rosterType.Name, excludeId: id);

        rosterType.IsActive = isActive;
        rosterType.UpdatedAt = DateTimeOffset.UtcNow;
        rosterType.UpdatedBy = userId;

        await _rosterTypeHandler.Update(rosterType);
        return ToDto(rosterType);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        RosterType rosterType = await _rosterTypeHandler.GetById(organizationId, id);
        if (rosterType == null)
            throw new NotFoundAppException("RosterType", id);

        bool isReferenced = await _rosterTypeHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Vrstu rostera koristi barem jedan zapis i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _rosterTypeHandler.Delete(rosterType);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _rosterTypeHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna vrsta rostera s nazivom '{name}' već postoji.");
    }

    private static RosterTypeDto ToDto(RosterType rosterType)
    {
        return new RosterTypeDto
        {
            Id = rosterType.Id.GetValueOrDefault(),
            Name = rosterType.Name,
            ColorHex = rosterType.ColorHex,
            CountsAsWork = rosterType.CountsAsWork,
            IsAbsence = rosterType.IsAbsence,
            RequiresTime = rosterType.RequiresTime,
            IsActive = rosterType.IsActive,
            SortOrder = rosterType.SortOrder,
            CreatedAt = rosterType.CreatedAt,
            CreatedBy = rosterType.CreatedBy,
            UpdatedAt = rosterType.UpdatedAt,
            UpdatedBy = rosterType.UpdatedBy
        };
    }
}
