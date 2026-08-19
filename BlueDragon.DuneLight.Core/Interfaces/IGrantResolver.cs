using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces;

/// <summary>Razrješava efektivne ovlasti korisnika po zahtjevu (agregacija GrantGroup dodjela iz baze, s kratkotrajnim cacheom).</summary>
public interface IGrantResolver
{
    Task<GrantContext> Resolve(Guid organizationId, Guid userId);
}