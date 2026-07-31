using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientTagHandler
{
    Task<(List<ClientTag> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<ClientTag> GetById(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — izbjegava N+1 kod validacije liste oznaka.</summary>
    Task<List<ClientTag>> GetByIds(Guid organizationId, List<Guid> ids);
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
    Task Add(ClientTag tag);
    Task Update(ClientTag tag);
    Task Delete(ClientTag tag);
    Task<bool> IsReferenced(Guid organizationId, Guid id);
}
