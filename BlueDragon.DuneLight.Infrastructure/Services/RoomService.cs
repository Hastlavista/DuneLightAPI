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

public class RoomService : IRoomService
{
    private readonly IRoomHandler _roomHandler;
    private readonly ICompanyHandler _companyHandler;

    public RoomService(IRoomHandler roomHandler, ICompanyHandler companyHandler)
    {
        _roomHandler = roomHandler;
        _companyHandler = companyHandler;
    }

    public async Task<PagedResult<RoomDto>> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request)
    {
        (List<Room> items, int totalCount) = await _roomHandler.GetPaged(organizationId, companyId, request);
        return PagedResult<RoomDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<RoomDto> GetById(Guid organizationId, Guid id)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        return ToDto(room);
    }

    public async Task<RoomDto> Create(Guid organizationId, Guid userId, RoomCreateRequest request)
    {
        await EnsureCompanyExists(organizationId, request.CompanyId);

        Room room = new Room
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CompanyId = request.CompanyId,
            Name = request.Name,
            AllowConcurrentBookings = request.AllowConcurrentBookings,
            Note = request.Note,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _roomHandler.Add(room);
        return await GetById(organizationId, room.Id.GetValueOrDefault());
    }

    public async Task<RoomDto> Update(Guid organizationId, Guid userId, Guid id, RoomUpdateRequest request)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        await EnsureCompanyExists(organizationId, request.CompanyId);

        room.CompanyId = request.CompanyId;
        room.Name = request.Name;
        room.AllowConcurrentBookings = request.AllowConcurrentBookings;
        room.Note = request.Note;
        room.SortOrder = request.SortOrder;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        room.UpdatedBy = userId;

        await _roomHandler.Update(room);
        return await GetById(organizationId, id);
    }

    public async Task<RoomDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        room.IsActive = isActive;
        room.UpdatedAt = DateTimeOffset.UtcNow;
        room.UpdatedBy = userId;

        await _roomHandler.Update(room);
        return ToDto(room);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        bool isReferenced = await _roomHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Prostorija je korištena na terminu ili grupi i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _roomHandler.Delete(room);
    }

    private async Task EnsureCompanyExists(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);
    }

    private static RoomDto ToDto(Room room)
    {
        return new RoomDto
        {
            Id = room.Id.GetValueOrDefault(),
            CompanyId = room.CompanyId,
            CompanyName = room.Company?.Name,
            Name = room.Name,
            AllowConcurrentBookings = room.AllowConcurrentBookings,
            IsActive = room.IsActive,
            Note = room.Note,
            SortOrder = room.SortOrder,
            CreatedAt = room.CreatedAt,
            CreatedBy = room.CreatedBy,
            UpdatedAt = room.UpdatedAt,
            UpdatedBy = room.UpdatedBy
        };
    }
}
