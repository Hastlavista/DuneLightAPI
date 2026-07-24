using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRosterEntryHandler
{
    Task<(List<RosterEntry> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? employeeId, Guid? rosterTypeId, DateTimeOffset? from, DateTimeOffset? to);

    /// <summary>Puni redak s Employee/RosterType uključenim — za prikaz.</summary>
    Task<RosterEntry> GetById(Guid organizationId, Guid id);

    /// <summary>Bare redak bez navigacija — za pripremu mutacije (Update/Delete).</summary>
    Task<RosterEntry> GetByIdLight(Guid organizationId, Guid id);

    /// <summary>Grubi vremenski prozor kandidata istog zaposlenika (precizna provjera preklapanja radi se u servisu). RosterType uključen (treba IsAbsence).</summary>
    Task<List<RosterEntry>> GetOverlapCandidates(
        Guid organizationId, Guid employeeId, DateTimeOffset windowFrom, DateTimeOffset windowTo, Guid? excludeId);

    /// <summary>Svi zapisi zaposlenika(-ka) čiji raspon dodiruje traženo razdoblje — za preglede. RosterType uključen.</summary>
    Task<List<RosterEntry>> GetForPeriod(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset periodFrom, DateTimeOffset periodTo);

    Task Add(RosterEntry entry);
    Task Update(RosterEntry entry);
    Task Delete(RosterEntry entry);
}
