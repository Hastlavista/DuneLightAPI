using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>One raw grant-key + the CapabilityGrantRole it plays inside a single CapabilityDefinition — the
/// minimal input the materialization algorithm needs per capability grant row.</summary>
public record CapabilityGrantRoleEntry(string GrantKey, CapabilityGrantRole Role);

/// <summary>
/// FAZA 1 Part I — razrješava ODABRANI opseg jedne capability-ja (CapabilitySelectedScope) u konkretan skup raw
/// grant-ključeva, koristeći isključivo ScopeModel + Role tablicu (CapabilityDefinitionGrant), bez posebnog
/// slučaja po capability-ju. Čisto/bez stanja — ne dira bazu, ne zna za GrantGroup. Rezultat NIJE runtime
/// autorizacija; runtime i dalje čita isključivo GrantGroupGrant.
/// </summary>
public interface ICapabilityMaterializationService
{
    /// <summary>Vraća raw grant-ključeve koje SelectedScope aktivira za jednu capability, prema njenom ScopeModel:
    /// None (On-&gt;PrimaryNoScope), ViewManage (View-&gt;PrimaryViewOnly; Manage-&gt;PrimaryViewOnly+PrimaryManage),
    /// OwnAll (Own-&gt;PrimaryOwn; All-&gt;PrimaryAll, NIKAD Own), ViewOwnAll (View-&gt;PrimaryViewOnly;
    /// Own-&gt;PrimaryViewOnly+PrimaryOwn; All-&gt;PrimaryViewOnly+PrimaryAll). MandatorySupporting retci se uključuju
    /// uvijek kad SelectedScope != None. SelectedScope=None uvijek vraća prazan skup.</summary>
    HashSet<string> Materialize(CapabilityScopeModel scopeModel, CapabilitySelectedScope selectedScope, IReadOnlyList<CapabilityGrantRoleEntry> grants);
}
