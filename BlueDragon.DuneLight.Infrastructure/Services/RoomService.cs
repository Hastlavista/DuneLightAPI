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
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

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
    private readonly ISchedulingOccupancyHandler _schedulingOccupancyHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly TimeProvider _timeProvider;

    public RoomService(
        IRoomHandler roomHandler, ICompanyHandler companyHandler, ISchedulingOccupancyHandler schedulingOccupancyHandler,
        IUnitOfWorkFactory unitOfWorkFactory, TimeProvider timeProvider)
    {
        _roomHandler = roomHandler;
        _companyHandler = companyHandler;
        _schedulingOccupancyHandler = schedulingOccupancyHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
        _timeProvider = timeProvider;
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
        EnsureCapacityIsValid(request.Capacity);

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
            Capacity = request.Capacity,
            Note = request.Note,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = userId
        };

        await _roomHandler.Add(room);
        return await GetById(organizationId, room.Id.GetValueOrDefault());
    }

    public async Task<RoomDto> Update(Guid organizationId, Guid userId, Guid id, RoomUpdateRequest request)
    {
        Room existing = await _roomHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Room", id);

        string name = request.Name?.Trim();
        EnsureCapacityIsValid(request.Capacity);
        await EnsureNameIsUnique(organizationId, existing.CompanyId, name, excludeId: id);

        // Phase M1D.1: kapacitet je TVRDA invarijanta zakazivanja — izmjena uzima ISTI lock subjekta rasporeda kao upisi
        // zakazivanja (SchedulingLockOrder; jedini lock ove transakcije), tek zatim čita trenutni kapacitet i (kod smanjenja)
        // vršnu tekuću/buduću zauzetost; commit dok je lock još držan. Povećanje je uvijek dopušteno.
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await _schedulingOccupancyHandler.LockSchedulingSubjects(uow, null, null, new[] { id });
            Room room = await _roomHandler.GetForCapacityChange(uow, organizationId, id);
            if (room == null)
                throw new NotFoundAppException("Room", id);

            await CapacityChangeGuard.EnsureRoomCapacityChange(
                _schedulingOccupancyHandler, uow, organizationId, id, room.Name, room.Capacity, request.Capacity, _timeProvider.GetUtcNow());

            // Id, OrganizationId i CompanyId se namjerno ne diraju — Room nikad ne mijenja poslovnicu.
            room.Name = name;
            room.Capacity = request.Capacity;
            room.Note = request.Note;
            room.SortOrder = request.SortOrder;
            room.UpdatedAt = _timeProvider.GetUtcNow();
            room.UpdatedBy = userId;
            await uow.CommitAsync();
        }

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
            room.UpdatedAt = _timeProvider.GetUtcNow();
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
            room.UpdatedAt = _timeProvider.GetUtcNow();
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

    /// <summary>Capacity = broj osoba istovremeno u prostoriji, ≥ 1 (isto kao CHECK u bazi) — jedino pravilo prostorije u
    /// zakazivanju (vidi Room.Capacity).</summary>
    private static void EnsureCapacityIsValid(int capacity)
    {
        if (!CatalogCapacity.IsValid(capacity))
            throw new ValidationAppException("Kapacitet prostorije mora biti najmanje 1 osoba.");
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
            Capacity = room.Capacity,
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
