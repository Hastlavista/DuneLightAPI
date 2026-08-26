using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRosterTypeHandler
{
    Task<(List<RosterType> Items, int TotalCount)> GetPaged(Guid organizationId, PagedRequest request);
    Task<RosterType> GetById(Guid organizationId, Guid id);
    Task<bool> NameExistsAmongActive(Guid organizationId, string name, Guid? excludeId);
    Task Add(RosterType rosterType);
    Task Update(RosterType rosterType);
    Task Delete(RosterType rosterType);
    Task<bool> IsReferenced(Guid organizationId, Guid id);

    /// <summary>Seed-a tri default RosterType zapisa (Rad/Godišnji/Bolovanje, vidi DefaultRosterTypes) za novu
    /// organizaciju, unutar iste transakcije kao AuthService.Register.</summary>
    Task SeedDefaultTypes(IUnitOfWork uow, Guid organizationId);
}
