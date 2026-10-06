using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi IGrantGroupCapabilityAuthoringService. Capability je samo editorska projekcija nad raw grantovima
/// (ADR-0023): pri spremanju se odabiri iz statičnog CapabilityCatalog-a prevode u raw grantove i sprema se samo konačni
/// GrantGroupGrant skup (preko IGrantGroupService — ista validacija naziva/ključeva i permissions.manage zaštita kao
/// obični CRUD); authoring-state se pri čitanju izvodi iz tih grantova.</summary>
public class GrantGroupCapabilityAuthoringService : IGrantGroupCapabilityAuthoringService
{
    private static readonly HashSet<string> ValidGrantKeys = Grants.Catalog.Select(g => g.Key).ToHashSet();

    private readonly IGrantGroupService _grantGroupService;
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;

    public GrantGroupCapabilityAuthoringService(IGrantGroupService grantGroupService, ICapabilityMaterializationService capabilityMaterializationService)
    {
        _grantGroupService = grantGroupService;
        _capabilityMaterializationService = capabilityMaterializationService;
    }

    public async Task<GrantGroupAuthoringDto> Create(Guid organizationId, Guid userId, GrantGroupCapabilityWriteRequest request)
    {
        List<string> grantKeys = ResolveFinalGrantSet(request);
        GrantGroupDto created = await _grantGroupService.Create(organizationId, userId,
            new GrantGroupCreateRequest { Name = request.Name, Grants = grantKeys });
        return Derive(created);
    }

    public async Task<GrantGroupAuthoringDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupCapabilityWriteRequest request)
    {
        List<string> grantKeys = ResolveFinalGrantSet(request);
        GrantGroupDto updated = await _grantGroupService.Update(organizationId, userId, id,
            new GrantGroupUpdateRequest { Name = request.Name, Grants = grantKeys });
        return Derive(updated);
    }

    public async Task<GrantGroupAuthoringDto> GetAuthoringState(Guid organizationId, Guid id)
    {
        GrantGroupDto group = await _grantGroupService.GetById(organizationId, id);
        return Derive(group);
    }

    /// <summary>Za svaki capability iz kataloga bira se NAJVEĆI opseg čiji je materijalizirani skup grantova u cijelosti
    /// sadržan u grupi; grantovi koje nijedan izabrani opseg ne objašnjava su ručni. Npr. grupa s {a.view, a.manage} je
    /// Manage bez obzira je li korisnik kliknuo View i ručno dodao a.manage — perzistirani skup je istina.</summary>
    private GrantGroupAuthoringDto Derive(GrantGroupDto group)
    {
        HashSet<string> groupGrants = group.Grants.ToHashSet();
        List<GrantGroupCapabilitySelectionDto> selections = new();
        HashSet<string> derived = new();

        foreach (CapabilityDefinition capability in CapabilityCatalog.All)
        {
            foreach (CapabilitySelectedScope scope in ScopesFromHighest(capability.ScopeModel))
            {
                HashSet<string> materialized = _capabilityMaterializationService.Materialize(capability.ScopeModel, scope, capability.Grants);
                if (materialized.Count == 0 || !materialized.IsSubsetOf(groupGrants))
                    continue;

                selections.Add(new GrantGroupCapabilitySelectionDto(capability.Key, scope));
                derived.UnionWith(materialized);
                break;
            }
        }

        return new GrantGroupAuthoringDto
        {
            GrantGroup = group,
            CapabilitySelections = selections,
            DerivedGrantKeys = derived.OrderBy(k => k).ToList(),
            ManualGrantKeys = groupGrants.Where(k => !derived.Contains(k)).OrderBy(k => k).ToList()
        };
    }

    private static IEnumerable<CapabilitySelectedScope> ScopesFromHighest(CapabilityScopeModel scopeModel) => scopeModel switch
    {
        CapabilityScopeModel.None => new[] { CapabilitySelectedScope.On },
        CapabilityScopeModel.ViewManage => new[] { CapabilitySelectedScope.Manage, CapabilitySelectedScope.View },
        CapabilityScopeModel.OwnAll => new[] { CapabilitySelectedScope.All, CapabilitySelectedScope.Own },
        CapabilityScopeModel.ViewOwnAll => new[] { CapabilitySelectedScope.All, CapabilitySelectedScope.Own, CapabilitySelectedScope.View },
        _ => throw new ArgumentOutOfRangeException(nameof(scopeModel), scopeModel, null)
    };

    /// <summary>Validira odabire (capability postoji, bez duplikata, opseg legalan za scope model) i ručne grantove
    /// (postoje u katalogu, ne preklapaju se s onim što odabiri već proizvode) te vraća konačni raw grant skup.</summary>
    private List<string> ResolveFinalGrantSet(GrantGroupCapabilityWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationAppException("Naziv grant-grupe je obavezan.");

        HashSet<string> seenKeys = new();
        HashSet<string> capabilityDerived = new();

        foreach (GrantGroupCapabilitySelectionRequest selection in request.CapabilitySelections)
        {
            if (!seenKeys.Add(selection.CapabilityKey))
                throw new BusinessRuleException(ErrorCodes.DuplicateCapabilitySelection, $"Capability '{selection.CapabilityKey}' je odabrana više puta.");

            CapabilityDefinition capability = CapabilityCatalog.Find(selection.CapabilityKey);
            if (capability == null)
                throw new BusinessRuleException(ErrorCodes.CapabilityUnknown, $"Nepoznata capability '{selection.CapabilityKey}'.");

            ValidateScopeLegal(capability.ScopeModel, selection.SelectedScope, capability.Key);
            capabilityDerived.UnionWith(_capabilityMaterializationService.Materialize(capability.ScopeModel, selection.SelectedScope, capability.Grants));
        }

        HashSet<string> manual = request.ManualGrantKeys.Distinct().ToHashSet();

        List<string> unknown = manual.Where(key => !ValidGrantKeys.Contains(key)).ToList();
        if (unknown.Count > 0)
            throw new ValidationAppException(ErrorCodes.GrantKeyUnknown, $"Nepoznati grant-ključevi: {string.Join(", ", unknown)}.");

        // Odbijanje umjesto tihe normalizacije — preklapanje otkriva bug u klijentu.
        List<string> alreadyDerived = manual.Where(capabilityDerived.Contains).ToList();
        if (alreadyDerived.Count > 0)
            throw new BusinessRuleException(ErrorCodes.GrantAlreadyCapabilityDerived, $"Grant(ovi) već proizlaze iz odabranih capability-ja, ne mogu se ručno dodati: {string.Join(", ", alreadyDerived)}.");

        return capabilityDerived.Union(manual).OrderBy(k => k).ToList();
    }

    private static void ValidateScopeLegal(CapabilityScopeModel scopeModel, CapabilitySelectedScope scope, string capabilityKey)
    {
        if (scope == CapabilitySelectedScope.None)
            return;

        bool legal = scopeModel switch
        {
            CapabilityScopeModel.None => scope == CapabilitySelectedScope.On,
            CapabilityScopeModel.ViewManage => scope is CapabilitySelectedScope.View or CapabilitySelectedScope.Manage,
            CapabilityScopeModel.OwnAll => scope is CapabilitySelectedScope.Own or CapabilitySelectedScope.All,
            CapabilityScopeModel.ViewOwnAll => scope is CapabilitySelectedScope.View or CapabilitySelectedScope.Own or CapabilitySelectedScope.All,
            _ => false
        };

        if (!legal)
            throw new BusinessRuleException(ErrorCodes.CapabilityScopeIllegal, $"Opseg '{scope}' nije dopušten za capability '{capabilityKey}' (ScopeModel: {scopeModel}).");
    }
}
