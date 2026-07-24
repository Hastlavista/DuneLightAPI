using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ClientTagService : IClientTagService
{
    private readonly IClientTagHandler _clientTagHandler;

    public ClientTagService(IClientTagHandler clientTagHandler)
    {
        _clientTagHandler = clientTagHandler;
    }

    public async Task<PagedResult<ClientTagDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<ClientTag> items, int totalCount) = await _clientTagHandler.GetPaged(organizationId, request);
        return PagedResult<ClientTagDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ClientTagDto> GetById(Guid organizationId, Guid id)
    {
        ClientTag tag = await _clientTagHandler.GetById(organizationId, id);
        if (tag == null)
            throw new NotFoundAppException("ClientTag", id);

        return ToDto(tag);
    }

    public async Task<ClientTagDto> Create(Guid organizationId, Guid userId, ClientTagCreateRequest request)
    {
        await EnsureNameIsUnique(organizationId, request.Name, excludeId: null);

        ClientTag tag = new ClientTag
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            ColorHex = request.ColorHex,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _clientTagHandler.Add(tag);
        return ToDto(tag);
    }

    public async Task<ClientTagDto> Update(Guid organizationId, Guid userId, Guid id, ClientTagUpdateRequest request)
    {
        ClientTag tag = await _clientTagHandler.GetById(organizationId, id);
        if (tag == null)
            throw new NotFoundAppException("ClientTag", id);

        await EnsureNameIsUnique(organizationId, request.Name, excludeId: id);

        tag.Name = request.Name;
        tag.ColorHex = request.ColorHex;
        tag.SortOrder = request.SortOrder;
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        tag.UpdatedBy = userId;

        await _clientTagHandler.Update(tag);
        return ToDto(tag);
    }

    public async Task<ClientTagDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        ClientTag tag = await _clientTagHandler.GetById(organizationId, id);
        if (tag == null)
            throw new NotFoundAppException("ClientTag", id);

        if (isActive)
            await EnsureNameIsUnique(organizationId, tag.Name, excludeId: id);

        tag.IsActive = isActive;
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        tag.UpdatedBy = userId;

        await _clientTagHandler.Update(tag);
        return ToDto(tag);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        ClientTag tag = await _clientTagHandler.GetById(organizationId, id);
        if (tag == null)
            throw new NotFoundAppException("ClientTag", id);

        bool isReferenced = await _clientTagHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Oznaku koristi barem jedan klijent i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _clientTagHandler.Delete(tag);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _clientTagHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna oznaka s nazivom '{name}' već postoji.");
    }

    private static ClientTagDto ToDto(ClientTag tag)
    {
        return new ClientTagDto
        {
            Id = tag.Id.GetValueOrDefault(),
            Name = tag.Name,
            ColorHex = tag.ColorHex,
            IsActive = tag.IsActive,
            SortOrder = tag.SortOrder,
            CreatedAt = tag.CreatedAt,
            CreatedBy = tag.CreatedBy,
            UpdatedAt = tag.UpdatedAt,
            UpdatedBy = tag.UpdatedBy
        };
    }
}
