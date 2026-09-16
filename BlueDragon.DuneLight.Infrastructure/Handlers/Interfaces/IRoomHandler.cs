using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRoomHandler
{
    Task<(List<Room> Items, int TotalCount)> GetPaged(Guid organizationId, Guid? companyId, PagedRequest request);
    Task<Room> GetById(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — izbjegava N+1 kod validacije liste prostorija.</summary>
    Task<List<Room>> GetByIds(Guid organizationId, List<Guid> ids);
    Task Add(Room room);
    Task Update(Room room);
    Task Delete(Room room);

    /// <summary>Naziv se uspoređuje normalizirano (trim + case-insensitive) unutar iste Company, isto kao
    /// ux_rooms_org_company_name_active.</summary>
    Task<bool> NameExistsAmongActive(Guid organizationId, Guid companyId, string name, Guid? excludeId);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
