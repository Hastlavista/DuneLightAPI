using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;

namespace BlueDragon.DuneLight.Core.Interfaces.Diagnostics;

/// <summary>
/// Read-only platform/development diagnostika grant sustava (FAZA 1 Part F) — NIJE tenant runtime autorizacija
/// i ne mijenja ništa. Uspoređuje Grants.Catalog, RequireGrant metapodatke otkrivene preko
/// IEndpointGrantMetadataProvider, DefaultGrantGroups predloške i (gdje je sigurno prepoznatljivo) postojeće
/// GrantGroup-e u bazi, radi ranog otkrivanja praznina/driftova prije nego što CapabilityDefinition/template
/// sustav (buduća faza) na njih legne.
/// </summary>
public interface IGrantDiagnosticsService
{
    Task<GrantDiagnosticsReport> GenerateReport();
}
