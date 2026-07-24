using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Clients;

public interface IClientService
{
    Task<PagedResult<ClientDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeLocationId,
        bool mineFirst, Guid currentUserId);

    Task<ClientDto> GetById(Guid organizationId, Guid id);
    Task<ClientDto> Create(Guid organizationId, Guid userId, ClientCreateRequest request);
    Task<ClientDto> Update(Guid organizationId, Guid userId, Guid id, ClientUpdateRequest request);
    Task<ClientDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
    Task<ClientDto> Anonymize(Guid organizationId, Guid userId, Guid id);

    /// <summary>Klijenti kojima je rođendan u zadanom razdoblju, sortirano po datumu. Godina se zanemaruje.</summary>
    Task<List<ClientBirthdayDto>> GetBirthdays(Guid organizationId, DateTimeOffset from, DateTimeOffset to);

    /// <summary>Prijedlog sljedećeg slobodnog broja člana — admin/trener ga može promijeniti.</summary>
    Task<int> GetNextMemberNumberSuggestion(Guid organizationId);
}
