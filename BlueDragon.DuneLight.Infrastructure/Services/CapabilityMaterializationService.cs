using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi ICapabilityMaterializationService — čista funkcija, bez stanja/ovisnosti.</summary>
public class CapabilityMaterializationService : ICapabilityMaterializationService
{
    public HashSet<string> Materialize(CapabilityScopeModel scopeModel, CapabilitySelectedScope selectedScope, IReadOnlyList<CapabilityGrantRoleEntry> grants)
    {
        if (selectedScope == CapabilitySelectedScope.None)
            return new HashSet<string>();

        HashSet<CapabilityGrantRole> activeRoles = ResolveActiveRoles(scopeModel, selectedScope);
        activeRoles.Add(CapabilityGrantRole.MandatorySupporting);

        return grants
            .Where(g => activeRoles.Contains(g.Role))
            .Select(g => g.GrantKey)
            .ToHashSet();
    }

    private static HashSet<CapabilityGrantRole> ResolveActiveRoles(CapabilityScopeModel scopeModel, CapabilitySelectedScope selectedScope)
    {
        switch (scopeModel)
        {
            case CapabilityScopeModel.None:
                if (selectedScope != CapabilitySelectedScope.On)
                    throw new ArgumentException($"SelectedScope '{selectedScope}' nije legalan za ScopeModel.None (jedino On/None).");
                return new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryNoScope };

            case CapabilityScopeModel.ViewManage:
                return selectedScope switch
                {
                    CapabilitySelectedScope.View => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryViewOnly },
                    CapabilitySelectedScope.Manage => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryViewOnly, CapabilityGrantRole.PrimaryManage },
                    _ => throw new ArgumentException($"SelectedScope '{selectedScope}' nije legalan za ScopeModel.ViewManage (View/Manage/None).")
                };

            case CapabilityScopeModel.OwnAll:
                return selectedScope switch
                {
                    // Namjerno BEZ PrimaryViewOnly — OwnAll model nema bazni pogled, samo Own/All (vidi FAZA 1 Part I).
                    CapabilitySelectedScope.Own => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryOwn },
                    CapabilitySelectedScope.All => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryAll },
                    _ => throw new ArgumentException($"SelectedScope '{selectedScope}' nije legalan za ScopeModel.OwnAll (Own/All/None).")
                };

            case CapabilityScopeModel.ViewOwnAll:
                return selectedScope switch
                {
                    CapabilitySelectedScope.View => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryViewOnly },
                    CapabilitySelectedScope.Own => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryViewOnly, CapabilityGrantRole.PrimaryOwn },
                    // All ne materijalizira Own — namjerno (vidi FAZA 1 Part I "IMPORTANT").
                    CapabilitySelectedScope.All => new HashSet<CapabilityGrantRole> { CapabilityGrantRole.PrimaryViewOnly, CapabilityGrantRole.PrimaryAll },
                    _ => throw new ArgumentException($"SelectedScope '{selectedScope}' nije legalan za ScopeModel.ViewOwnAll (View/Own/All/None).")
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(scopeModel), scopeModel, null);
        }
    }
}
