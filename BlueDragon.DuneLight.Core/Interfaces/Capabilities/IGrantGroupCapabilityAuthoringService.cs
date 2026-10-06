using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>Capability-aware autorstvo GrantGroup-a (ADR-0023). Klijent deklarira namjeru (capabilityji + opsezi +
/// dodatni ručni grantovi), backend iz statičnog CapabilityCatalog-a računa konačni raw grant skup i sprema SAMO njega
/// (GrantGroupGrant). Authoring-state se pri čitanju izvodi iz grantova; odabiri se nigdje ne pamte. Runtime
/// autorizacija ostaje isključivo na raw grantovima.</summary>
public interface IGrantGroupCapabilityAuthoringService
{
    Task<GrantGroupAuthoringDto> Create(Guid organizationId, Guid userId, GrantGroupCapabilityWriteRequest request);
    Task<GrantGroupAuthoringDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupCapabilityWriteRequest request);

    /// <summary>Izvodi authoring-state iz trenutnih grantova grupe — ništa ne piše u bazu.</summary>
    Task<GrantGroupAuthoringDto> GetAuthoringState(Guid organizationId, Guid id);
}
