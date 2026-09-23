using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>FAZA 2 — capability-aware autorstvo GrantGroup-a (create/update/read-back). Backend je jedini
/// autoritativan izvor konačnog raw grant skupa; klijent deklarira NAMJERU (capabilities + scope-ovi + legitimni
/// ručni grantovi), nikad gotov materijalizirani skup. Zaštićeno permissions.manage grantom (vidi GrantGroupsController RequireGrant).
/// Runtime autorizacija ostaje isključivo GrantGroupGrant/GrantResolver — ovaj servis samo piše u tu tablicu preko
/// IGrantGroupHandler.ApplyCapabilitySelections.</summary>
public interface IGrantGroupCapabilityAuthoringService
{
    Task<GrantGroupAuthoringDto> Create(Guid organizationId, Guid userId, GrantGroupCapabilityWriteRequest request);
    Task<GrantGroupAuthoringDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupCapabilityWriteRequest request);

    /// <summary>FAZA 2 Part I/J — čita TRENUTNO autoritativno stanje. Za legacy/drifted grupu (bez snapshot
    /// metapodataka) NIKAD ne izmišlja capability odabire niti ovaj poziv ne piše ništa u bazu — vidi
    /// GrantGroupAuthoringDto.HasCapabilityMetadata.</summary>
    Task<GrantGroupAuthoringDto> GetAuthoringState(Guid organizationId, Guid id);
}
