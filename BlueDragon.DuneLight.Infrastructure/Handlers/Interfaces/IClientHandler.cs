using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientHandler
{
    Task<(List<Client> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeLocationId,
        Guid? mineFirstEmployeeId);

    /// <summary>Puni graf (HomeLocation, HomeTrainer, Tags) — za prikaz/čitanje.</summary>
    Task<Client> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije.</summary>
    Task<Client> GetByIdLight(Guid organizationId, Guid id);

    Task Add(Client client);

    /// <summary>`client` NE SMIJE imati popunjenu Tags navigacijsku kolekciju (koristiti GetByIdLight).</summary>
    Task Update(Client client, List<ClientTagAssignment> newTags);

    Task SetActiveAndStamp(Guid clientId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy);

    /// <summary>GDPR pravo na zaborav — briše osobne/zdravstvene podatke i sve oznake, čuva Id/MemberNumber.</summary>
    Task Anonymize(Guid clientId, DateTimeOffset anonymizedAt, Guid? anonymizedBy);

    Task Delete(Client client);

    Task<bool> IsMemberNumberTaken(Guid organizationId, int memberNumber, Guid? excludeId);
    Task<int> GetNextMemberNumber(Guid organizationId);

    /// <summary>Lagana projekcija aktivnih klijenata s postavljenim datumom rođenja — za obradu u memoriji.</summary>
    Task<List<Client>> GetBirthdayCandidates(Guid organizationId);
}
