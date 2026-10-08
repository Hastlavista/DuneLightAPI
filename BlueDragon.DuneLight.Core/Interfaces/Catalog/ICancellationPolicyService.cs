using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

/// <summary>
/// P1 (ADR-0015) — upravljanje politikama otkazivanja: imenovani profili, nepromjenjive verzije (kreiranje = objava),
/// dodjele po scopeu (Company+Service, Service, Company) i zadana politika organizacije. Profil koji je zadani ili ima
/// dodjelu ne može se deaktivirati (CANCELLATION_POLICY_IN_USE); neaktivan se ne može dodijeliti ni postaviti zadanim.
/// </summary>
public interface ICancellationPolicyService
{
    Task<List<CancellationPolicyDto>> GetAll(Guid organizationId);
    Task<CancellationPolicyDto> GetById(Guid organizationId, Guid id);
    Task<CancellationPolicyDto> Create(Guid organizationId, Guid userId, CancellationPolicyCreateRequest request);
    Task<CancellationPolicyDto> Rename(Guid organizationId, Guid userId, Guid id, CancellationPolicyRenameRequest request);

    /// <summary>Objavljuje novu verziju (Version + 1); starije verzije se nikad ne mijenjaju ni brišu.</summary>
    Task<CancellationPolicyDto> PublishVersion(Guid organizationId, Guid userId, Guid id, CancellationPolicyRulesRequest request);

    Task<CancellationPolicyDto> Activate(Guid organizationId, Guid userId, Guid id);
    Task<CancellationPolicyDto> Deactivate(Guid organizationId, Guid userId, Guid id);

    /// <summary>Postavlja zadanu politiku organizacije (zamjenjuje dosadašnju).</summary>
    Task<CancellationPolicyDto> SetOrganizationDefault(Guid organizationId, Guid userId, CancellationPolicyDefaultRequest request);

    Task<List<CancellationPolicyAssignmentDto>> GetAssignments(Guid organizationId);

    /// <summary>Dodjela po scopeu (upsert: postojeća dodjela istog scopea dobiva novi profil).</summary>
    Task<CancellationPolicyAssignmentDto> Assign(Guid organizationId, Guid userId, CancellationPolicyAssignmentRequest request);

    Task RemoveAssignment(Guid organizationId, Guid assignmentId);

    /// <summary>Pregled: koja bi politika (profil + najnovija verzija) sada vrijedila za poslovnicu i uslugu.</summary>
    Task<CancellationPolicyResolutionDto> Resolve(Guid organizationId, Guid companyId, Guid serviceId);
}
