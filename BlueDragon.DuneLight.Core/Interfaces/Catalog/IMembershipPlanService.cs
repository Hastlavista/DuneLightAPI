using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

/// <summary>
/// P2 (faza 2A) — katalog planova članarina: profil s nepromjenjivim verzijama uvjeta (cijena, interval, način obnove,
/// opseg poslovnica, pokrivene usluge, limiti, obveza, otkazni rok, pauza). Plan se ne briše, samo deaktivira.
/// </summary>
public interface IMembershipPlanService
{
    Task<List<MembershipPlanDto>> GetAll(Guid organizationId);
    Task<MembershipPlanDto> GetById(Guid organizationId, Guid id);
    Task<MembershipPlanDto> Create(Guid organizationId, Guid userId, MembershipPlanCreateRequest request);
    Task<MembershipPlanDto> UpdateDetails(Guid organizationId, Guid userId, Guid id, MembershipPlanDetailsRequest request);
    Task<MembershipPlanDto> UpdateCapacity(Guid organizationId, Guid userId, Guid id, MembershipPlanCapacityRequest request);

    /// <summary>Objavljuje novu verziju uvjeta (Version + 1); starije verzije se nikad ne mijenjaju. Uz NewSalesAndExisting
    /// zakazuje nove uvjete postojećim članstvima od prve obnove nakon roka najave (strogo povoljna izmjena: od sljedeće).</summary>
    Task<MembershipPlanVersionPublishResultDto> PublishVersion(Guid organizationId, Guid userId, Guid id, MembershipPlanVersionPublishRequest request);

    Task<MembershipPlanDto> Activate(Guid organizationId, Guid userId, Guid id);
    Task<MembershipPlanDto> Deactivate(Guid organizationId, Guid userId, Guid id);
}
