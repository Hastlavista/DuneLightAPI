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

/// <summary>
/// Room == fizička bookabilna prostorija unutar točno jedne Company (npr. "Masaža 1", "Pilates studio").
/// CompanyId se nakon kreiranja više ne mijenja — povijesni termini referenciraju Room, pa bi premještaj
/// prostorije u drugu poslovnicu iskrivio povijest. Za fizički premještaj: deaktivirati staru, kreirati
/// novu u ciljnoj Company. Isti obrazac kao CompanyService (immutable ownership, normalizirano ime, active
/// lifecycle), ali BEZ pravila "barem jedna aktivna" — poslovnica smije imati nula aktivnih prostorija.
/// </summary>
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
        string name = request.Name?.Trim();

        Company company = await EnsureCompanyExists(organizationId, request.CompanyId);
        if (!company.IsActive)
            throw new ValidationAppException($"Poslovnica '{company.Name}' nije aktivna — nova prostorija se ne može kreirati.");

        await EnsureNameIsUnique(organizationId, request.CompanyId, name, excludeId: null);

        Room room = new Room
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CompanyId = request.CompanyId,
            Name = name,
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

        string name = request.Name?.Trim();
        await EnsureNameIsUnique(organizationId, room.CompanyId, name, excludeId: id);

        // Id, OrganizationId i CompanyId se namjerno ne diraju — Room nikad ne mijenja poslovnicu (vidi
        // klasnu napomenu). Za fizički premještaj: deaktivirati ovu i kreirati novu u ciljnoj Company.
        room.Name = name;
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
        if (isActive)
            return await Reactivate(organizationId, userId, id);

        return await Deactivate(organizationId, userId, id);
    }

    private async Task<RoomDto> Reactivate(Guid organizationId, Guid userId, Guid id)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        if (!room.IsActive)
        {
            Company company = await _companyHandler.GetById(organizationId, room.CompanyId);
            if (company == null || !company.IsActive)
                throw new ValidationAppException($"Poslovnica prostorije '{room.Name}' nije aktivna — prostorija se ne može ponovno aktivirati.");

            // Naziv je mogao u međuvremenu "procuriti" na drugu aktivnu prostoriju u istoj Company dok je
            // ova bila neaktivna (djelomični unique indeks vrijedi samo WHERE is_active = true) — provjeri
            // prije povratka u pogon. Isti obrazac kao CompanyService.Reactivate.
            await EnsureNameIsUnique(organizationId, room.CompanyId, room.Name, excludeId: id);

            room.IsActive = true;
            room.UpdatedAt = DateTimeOffset.UtcNow;
            room.UpdatedBy = userId;
            await _roomHandler.Update(room);
        }

        return ToDto(room);
    }

    private async Task<RoomDto> Deactivate(Guid organizationId, Guid userId, Guid id)
    {
        Room room = await _roomHandler.GetById(organizationId, id);
        if (room == null)
            throw new NotFoundAppException("Room", id);

        // Za razliku od Company nema pravila "barem jedna aktivna" — poslovnica smije imati nula aktivnih
        // prostorija, pa nema potrebe za brave-lock/count logikom ovdje.
        if (room.IsActive)
        {
            room.IsActive = false;
            room.UpdatedAt = DateTimeOffset.UtcNow;
            room.UpdatedBy = userId;
            await _roomHandler.Update(room);
        }

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

    private async Task<Company> EnsureCompanyExists(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        return company;
    }

    private async Task EnsureNameIsUnique(Guid organizationId, Guid companyId, string name, Guid? excludeId)
    {
        bool exists = await _roomHandler.NameExistsAmongActive(organizationId, companyId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna prostorija s nazivom '{name}' već postoji u ovoj poslovnici.");
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
