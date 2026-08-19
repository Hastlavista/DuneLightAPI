using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Roster;

public interface IRosterEntryService
{
    Task<PagedResult<RosterEntryDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? employeeId, Guid? rosterTypeId, DateTimeOffset? from, DateTimeOffset? to);

    Task<RosterEntryDto> GetById(Guid organizationId, Guid id);

    Task<RosterEntryDto> Create(Guid organizationId, Guid userId, bool hasFullScope, RosterEntryCreateRequest request);

    Task<RosterEntryDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, RosterEntryUpdateRequest request);

    Task Delete(Guid organizationId, Guid userId, bool hasFullScope, Guid id);

    Task<RosterTeamMonthlyDto> GetTeamMonthly(Guid organizationId, int year, int month, Guid? companyId);

    Task<RosterPersonalReviewDto> GetPersonal(
        Guid organizationId, Guid userId, bool hasFullScope, Guid employeeId, DateTimeOffset from, DateTimeOffset to);
}
