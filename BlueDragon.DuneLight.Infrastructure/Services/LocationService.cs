using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class LocationService : ILocationService
{
    private readonly ILocationHandler _locationHandler;

    public LocationService(ILocationHandler locationHandler)
    {
        _locationHandler = locationHandler;
    }

    public async Task<PagedResult<LocationDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<Location> items, int totalCount) = await _locationHandler.GetPaged(organizationId, request);
        return PagedResult<LocationDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<LocationDto> GetById(Guid organizationId, Guid id)
    {
        Location location = await _locationHandler.GetById(organizationId, id);
        if (location == null)
            throw new NotFoundAppException("Location", id);

        return ToDto(location);
    }

    public async Task<LocationDto> Create(Guid organizationId, Guid userId, LocationCreateRequest request)
    {
        Location location = new Location
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            Address = request.Address,
            Phone = request.Phone,
            ColorHex = request.ColorHex,
            Note = request.Note,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _locationHandler.Add(location);
        return ToDto(location);
    }

    public async Task<LocationDto> Update(Guid organizationId, Guid userId, Guid id, LocationUpdateRequest request)
    {
        Location location = await _locationHandler.GetById(organizationId, id);
        if (location == null)
            throw new NotFoundAppException("Location", id);

        location.Name = request.Name;
        location.Address = request.Address;
        location.Phone = request.Phone;
        location.ColorHex = request.ColorHex;
        location.Note = request.Note;
        location.SortOrder = request.SortOrder;
        location.UpdatedAt = DateTimeOffset.UtcNow;
        location.UpdatedBy = userId;

        await _locationHandler.Update(location);
        return ToDto(location);
    }

    public async Task<LocationDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Location location = await _locationHandler.GetById(organizationId, id);
        if (location == null)
            throw new NotFoundAppException("Location", id);

        if (!isActive && location.IsActive)
        {
            int activeCount = await _locationHandler.CountActive(organizationId);
            if (activeCount <= 1)
                throw new BusinessRuleException(ErrorCodes.LastActiveLocation, "Mora postojati barem jedna aktivna lokacija.");
        }

        location.IsActive = isActive;
        location.UpdatedAt = DateTimeOffset.UtcNow;
        location.UpdatedBy = userId;

        await _locationHandler.Update(location);
        return ToDto(location);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Location location = await _locationHandler.GetById(organizationId, id);
        if (location == null)
            throw new NotFoundAppException("Location", id);

        bool isReferenced = await _locationHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Lokacija je korištena u cjeniku i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _locationHandler.Delete(location);
    }

    private static LocationDto ToDto(Location location)
    {
        return new LocationDto
        {
            Id = location.Id.GetValueOrDefault(),
            Name = location.Name,
            Address = location.Address,
            Phone = location.Phone,
            ColorHex = location.ColorHex,
            IsActive = location.IsActive,
            Note = location.Note,
            SortOrder = location.SortOrder,
            CreatedAt = location.CreatedAt,
            CreatedBy = location.CreatedBy,
            UpdatedAt = location.UpdatedAt,
            UpdatedBy = location.UpdatedBy
        };
    }
}
