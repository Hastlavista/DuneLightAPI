using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICompanyHandler
{
    Task<(List<Company> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<Company> GetById(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — izbjegava N+1 kod validacije liste tvrtka.</summary>
    Task<List<Company>> GetByIds(Guid organizationId, List<Guid> ids);
    Task Add(Company company);
    Task Update(Company company);
    Task Delete(Company company);
    Task<int> CountActive(Guid organizationId);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
