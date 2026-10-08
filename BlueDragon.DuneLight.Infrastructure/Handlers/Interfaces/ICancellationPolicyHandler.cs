using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>P1 (ADR-0015) — pristup bazi za profile politike otkazivanja, njihove verzije i dodjele.</summary>
public interface ICancellationPolicyHandler
{
    Task<List<CancellationPolicy>> GetAll(Guid organizationId);

    /// <summary>Profil sa svim verzijama, ili null.</summary>
    Task<CancellationPolicy> GetById(Guid organizationId, Guid id);

    /// <summary>Zaključava redak profila (FOR UPDATE) i vraća ga svježe učitanog (bez verzija), ili null. Serijalizira
    /// objavu verzije, (de)aktivaciju, dodjelu i postavljanje zadane politike nad istim profilom.</summary>
    Task<CancellationPolicy> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Zaključava i vraća trenutnu zadanu politiku organizacije, ili null.</summary>
    Task<CancellationPolicy> GetOrganizationDefaultForUpdate(IUnitOfWork uow, Guid organizationId);

    Task<int> GetLatestVersionNumber(IUnitOfWork uow, Guid policyId);

    Task<bool> HasAssignments(IUnitOfWork uow, Guid organizationId, Guid policyId);

    Task<bool> ActiveNameExists(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId);

    Task<List<CancellationPolicyAssignment>> GetAssignments(Guid organizationId);

    /// <summary>Dodjela za TOČNO zadani scope (Company+Service, samo Service ili samo Company), ili null.</summary>
    Task<CancellationPolicyAssignment> GetAssignmentForScope(IUnitOfWork uow, Guid organizationId, Guid? companyId, Guid? serviceId);

    Task<CancellationPolicyAssignment> GetAssignment(IUnitOfWork uow, Guid organizationId, Guid assignmentId);

    /// <summary>Kandidati razrješavanja za (poslovnica, usluga): dodjele scopeova Company+Service, Service i Company.</summary>
    Task<List<CancellationPolicyAssignment>> GetResolutionCandidates(IUnitOfWork uow, Guid organizationId, Guid companyId, Guid serviceId);

    Task<CancellationPolicy> GetOrganizationDefault(IUnitOfWork uow, Guid organizationId);

    /// <summary>Najnovija (trenutno važeća) verzija profila, ili null.</summary>
    Task<CancellationPolicyVersion> GetLatestVersion(IUnitOfWork uow, Guid policyId);
}
