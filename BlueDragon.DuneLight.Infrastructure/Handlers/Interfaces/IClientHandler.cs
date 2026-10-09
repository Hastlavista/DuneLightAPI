using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientHandler
{
    Task<(List<Client> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? tagId, Guid? homeTrainerId, Guid? homeCompanyId,
        Guid? mineFirstEmployeeId);

    /// <summary>Puni graf (HomeCompany, HomeTrainer, Tags) — za prikaz/čitanje.</summary>
    Task<Client> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije.</summary>
    Task<Client> GetByIdLight(Guid organizationId, Guid id);

    /// <summary>Batch dohvat po ID-evima u jednom upitu — koristi se za validaciju postojanja liste klijenata (izbjegava N+1).</summary>
    Task<List<Client>> GetByIds(Guid organizationId, List<Guid> ids);

    /// <summary>K1-3 — dodaje klijenta pod transakcijskim advisory lockom brojeva članova organizacije; uz
    /// <paramref name="assignMemberNumber"/> broj člana = najveći postojeći + 1 (1 za prvog klijenta), izračunat pod lockom.</summary>
    /// <remarks>T1-9: <paramref name="audit"/> (povijest klijenta) se upisuje u istoj transakciji, nakon klijenta.</remarks>
    Task Add(Client client, bool assignMemberNumber, IReadOnlyList<ClientAuditLog> audit = null);

    /// <summary>`client` NE SMIJE imati popunjenu Tags navigacijsku kolekciju (koristiti GetByIdLight).</summary>
    /// <remarks>T1-9: <paramref name="audit"/> se upisuje u istom SaveChanges kao i izmjena.</remarks>
    Task Update(Client client, List<ClientTagAssignment> newTags, IReadOnlyList<ClientAuditLog> audit = null);

    Task SetActiveAndStamp(Guid organizationId, Guid clientId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy);

    /// <summary>GDPR pravo na zaborav — briše osobne/zdravstvene podatke i sve oznake, čuva Id/MemberNumber.</summary>
    /// <remarks>T1-9: <paramref name="audit"/> se upisuje u istom SaveChanges kao i anonimizacija.</remarks>
    Task Anonymize(Guid organizationId, Guid clientId, DateTimeOffset anonymizedAt, Guid? anonymizedBy, IReadOnlyList<ClientAuditLog> audit = null);

    Task Delete(Client client);

    Task<bool> IsMemberNumberTaken(Guid organizationId, int memberNumber, Guid? excludeId);

    /// <summary>ADR-0020 — <paramref name="emailComparisonKey"/> je EmailNormalizer.ComparisonKey (trim + lowercase);
    /// uspoređuje se s lower(email), isto kao unique indeks ux_clients_organization_email.</summary>
    Task<bool> IsEmailTaken(Guid organizationId, string emailComparisonKey, Guid? excludeId);
    Task<int> GetNextMemberNumber(Guid organizationId);

    /// <summary>Lagana projekcija aktivnih klijenata s postavljenim datumom rođenja — za obradu u memoriji.</summary>
    Task<List<Client>> GetBirthdayCandidates(Guid organizationId);
}
