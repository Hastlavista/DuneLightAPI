using System.Collections.Generic;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;

namespace BlueDragon.DuneLight.Core.Interfaces.Diagnostics;

/// <summary>
/// Otkriva RequireGrant/RequireGrantOrAssignedCompany metapodatke po HTTP endpointu, preko
/// ASP.NET Core action-descriptora (vidi implementaciju u API sloju — EndpointGrantMetadataProvider). Sučelje
/// živi u Core da bi IGrantDiagnosticsService (Infrastructure) mogao ovisiti o njemu bez referenciranja API
/// projekta; implementacija je nužno u API sloju jer samo ondje postoje kontroleri i IActionDescriptorCollectionProvider.
/// Isključivo diagnostika — ne koristi se za runtime autorizacijske odluke.
/// </summary>
public interface IEndpointGrantMetadataProvider
{
    List<EndpointGrantMetadata> GetEndpointGrantMetadata();
}
