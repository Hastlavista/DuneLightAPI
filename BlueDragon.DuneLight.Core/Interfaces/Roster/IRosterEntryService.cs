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

    Task<RosterEntryDto> Create(Guid organizationId, Guid userId, bool isAdmin, RosterEntryCreateRequest request);

    Task<RosterEntryDto> Update(Guid organizationId, Guid userId, bool isAdmin, Guid id, RosterEntryUpdateRequest request);

    Task Delete(Guid organizationId, Guid userId, bool isAdmin, Guid id);

    Task<RosterTeamMonthlyDto> GetTeamMonthly(Guid organizationId, int year, int month, Guid? locationId);

    Task<RosterPersonalReviewDto> GetPersonal(
        Guid organizationId, Guid userId, bool isAdmin, Guid employeeId, DateTimeOffset from, DateTimeOffset to);
}
