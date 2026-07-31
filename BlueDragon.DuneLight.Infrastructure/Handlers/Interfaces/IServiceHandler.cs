using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IServiceHandler
{
    Task<(List<Service> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request, Guid? serviceCategoryId);
    Task<Service> GetById(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — izbjegava N+1 kod validacije liste usluga.</summary>
    Task<List<Service>> GetByIds(Guid organizationId, List<Guid> ids);
    Task<List<Service>> GetAllActive(Guid organizationId);
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
    Task Add(Service service);
    Task Update(Service service);
    Task Delete(Service service);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
    Task<bool> HasActiveServicesInCategory(Guid organizationId, Guid serviceCategoryId);
    Task<bool> IsCategoryReferenced(Guid organizationId, Guid serviceCategoryId);
}
