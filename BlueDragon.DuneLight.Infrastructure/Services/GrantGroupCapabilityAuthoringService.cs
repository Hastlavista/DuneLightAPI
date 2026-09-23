using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi IGrantGroupCapabilityAuthoringService — FAZA 2. Backend je autoritativan izvor konačnog raw grant
/// skupa; klijent šalje SAMO namjeru (capability odabiri + legitimni ručni grantovi). Persistencija/diffing algoritam
/// živi u GrantGroupHandler.ApplyCapabilitySelections (isti obrazac kao ApplyTemplate) — ovaj servis validira ulaz i
/// orkestrira jednu atomsku transakciju (IUnitOfWork, jedan SaveChangesAsync unutar ApplyCapabilitySelections).</summary>
public class GrantGroupCapabilityAuthoringService : IGrantGroupCapabilityAuthoringService
{
    private static readonly HashSet<string> ValidGrantKeys = Grants.Catalog.Select(g => g.Key).ToHashSet();

    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly ICapabilityDefinitionHandler _capabilityDefinitionHandler;
    private readonly IDefaultRoleTemplateHandler _defaultRoleTemplateHandler;
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IPermissionAdministrationSafetyService _permissionAdministrationSafetyService;

    public GrantGroupCapabilityAuthoringService(
        IGrantGroupHandler grantGroupHandler,
        ICapabilityDefinitionHandler capabilityDefinitionHandler,
        IDefaultRoleTemplateHandler defaultRoleTemplateHandler,
        ICapabilityMaterializationService capabilityMaterializationService,
        IUnitOfWorkFactory unitOfWorkFactory,
        IPermissionAdministrationSafetyService permissionAdministrationSafetyService)
    {
        _grantGroupHandler = grantGroupHandler;
        _capabilityDefinitionHandler = capabilityDefinitionHandler;
        _defaultRoleTemplateHandler = defaultRoleTemplateHandler;
        _capabilityMaterializationService = capabilityMaterializationService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _permissionAdministrationSafetyService = permissionAdministrationSafetyService;
    }

    public async Task<GrantGroupAuthoringDto> Create(Guid organizationId, Guid userId, GrantGroupCapabilityWriteRequest request)
    {
        ValidateName(request.Name);

        bool nameExists = await _grantGroupHandler.NameExists(organizationId, request.Name, excludeId: null);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Grant-grupa s ovim nazivom već postoji.");

        List<TemplateCapabilitySelection> selections = await ResolveAndValidateSelections(request.CapabilitySelections);
        HashSet<string> capabilityDerivedSet = MaterializeAll(selections);
        // Nova grupa nema prijašnju template-compatibility provenance (Part D korak 5 — TemplateCompatibilitySet = prazan za custom rolu).
        HashSet<string> manualGrantKeys = ValidateManualGrantKeys(request.ManualGrantKeys, capabilityDerivedSet, new HashSet<string>());

        Guid groupId = Guid.NewGuid();

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        GrantGroup group = new GrantGroup
        {
            Id = groupId,
            OrganizationId = organizationId,
            Name = request.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        // Grupa mora postojati PRIJE ApplyCapabilitySelections upiše djecu preko sirovog FK scalara — isti
        // obrazac kao GrantGroupHandler.EnsureDefaultGrantGroups.
        await _grantGroupHandler.Add(uow, group);
        await _grantGroupHandler.ApplyCapabilitySelections(uow, groupId, selections, manualGrantKeys, userId);

        await uow.CommitAsync();

        return await GetAuthoringState(organizationId, groupId);
    }

    public async Task<GrantGroupAuthoringDto> Update(Guid organizationId, Guid userId, Guid id, GrantGroupCapabilityWriteRequest request)
    {
        GrantGroup existing = await _grantGroupHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("GrantGroup", id);

        ValidateName(request.Name);

        bool nameExists = await _grantGroupHandler.NameExists(organizationId, request.Name, excludeId: id);
        if (nameExists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, "Grant-grupa s ovim nazivom već postoji.");

        // OldTemplateCompatibilitySet — potreban PRIJE transakcije da validacija ManualGrantKeys ispravno odbije
        // pokušaj ručnog "preuzimanja" grant-a koji već proizlazi iz sačuvane template-compatibility provenance
        // (vidi FAZA 2 Part G). ApplyCapabilitySelections unutar transakcije čita isti skup ponovno (autoritativno).
        List<GrantGroupTemplateGrant> currentTemplateGrants = await _grantGroupHandler.GetTemplateGrantProvenance(organizationId, id);
        HashSet<string> templateCompatibilitySet = currentTemplateGrants.Select(g => g.GrantKey).ToHashSet();

        List<TemplateCapabilitySelection> selections = await ResolveAndValidateSelections(request.CapabilitySelections);
        HashSet<string> capabilityDerivedSet = MaterializeAll(selections);
        HashSet<string> manualGrantKeys = ValidateManualGrantKeys(request.ManualGrantKeys, capabilityDerivedSet, templateCompatibilitySet);

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await _grantGroupHandler.UpdateMetadata(uow, id, request.Name, userId);
        await _grantGroupHandler.ApplyCapabilitySelections(uow, id, selections, manualGrantKeys, userId);

        // Part F — ovaj edit može ukloniti permissions.manage s grupe preko capability odabira/ručnih grantova;
        // ApplyCapabilitySelections je gore VEĆ upisao novi grant skup (svoj interni SaveChangesAsync), pa
        // provjera ovdje vidi stvarno novo stanje. Baca PRIJE uow.CommitAsync() — DisposeAsync bez commita radi
        // rollback, ništa se ne sprema ako bi organizacija ostala bez aktivnog permission-admina.
        await _permissionAdministrationSafetyService.EnsureRetainsPermissionAdminInTransaction(uow, organizationId);

        await uow.CommitAsync();

        return await GetAuthoringState(organizationId, id);
    }

    public async Task<GrantGroupAuthoringDto> GetAuthoringState(Guid organizationId, Guid id)
    {
        GrantGroup group = await _grantGroupHandler.GetById(organizationId, id);
        if (group == null)
            throw new NotFoundAppException("GrantGroup", id);

        List<GrantGroupCapabilitySnapshot> snapshots = await _grantGroupHandler.GetCapabilitySnapshots(organizationId, id);
        List<GrantGroupTemplateGrant> templateGrants = await _grantGroupHandler.GetTemplateGrantProvenance(organizationId, id);

        return await BuildAuthoringDto(group, snapshots, templateGrants);
    }

    private async Task<GrantGroupAuthoringDto> BuildAuthoringDto(GrantGroup group, List<GrantGroupCapabilitySnapshot> snapshots, List<GrantGroupTemplateGrant> templateGrants)
    {
        HashSet<string> capabilityDerivedSet = new();
        foreach (GrantGroupCapabilitySnapshot snapshot in snapshots)
        {
            List<CapabilityGrantRoleEntry> entries = snapshot.CapabilityDefinition.Grants
                .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role))
                .ToList();
            capabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(snapshot.CapabilityDefinition.ScopeModel, snapshot.SelectedScope, entries));
        }

        HashSet<string> templateCompatibilitySet = templateGrants.Select(g => g.GrantKey).ToHashSet();

        HashSet<string> derivedGrantKeys = new(capabilityDerivedSet);
        derivedGrantKeys.UnionWith(templateCompatibilitySet);

        bool hasCapabilityMetadata = snapshots.Count > 0;

        // FAZA 2 Part J — legacy/drifted grupa (bez snapshot metapodataka) NIKAD ne dobiva pretpostavljenu
        // Manual Advanced podjelu; njeni sirovi grantovi ostaju isključivo u GrantGroup.Grants dok Owner ne
        // napravi eksplicitni capability-aware save (koji JEST konverzija, vidi FAZA 2 Part J odluka).
        List<string> manualGrantKeys = hasCapabilityMetadata
            ? group.Grants.Select(g => g.GrantKey).Where(k => !derivedGrantKeys.Contains(k)).OrderBy(k => k).ToList()
            : new List<string>();

        string templateSourceKey = templateGrants.Select(g => g.SourceTemplateKey).FirstOrDefault()
            ?? snapshots.Select(s => s.SourceTemplateKey).FirstOrDefault(k => k != null);
        int? templateSourceVersion = templateGrants.Select(g => (int?)g.SourceTemplateVersion).FirstOrDefault()
            ?? snapshots.Select(s => s.SourceTemplateVersion).FirstOrDefault(v => v.HasValue);

        bool isCustomized = !hasCapabilityMetadata
            || await ComputeIsCustomized(templateSourceKey, templateSourceVersion, snapshots, templateCompatibilitySet, manualGrantKeys);

        return new GrantGroupAuthoringDto
        {
            GrantGroup = ToGrantGroupDto(group),
            CapabilitySelections = hasCapabilityMetadata
                ? snapshots.Select(s => new GrantGroupCapabilitySelectionDto(s.CapabilityDefinition.Key, s.CapabilityDefinition.Version, s.SelectedScope)).ToList()
                : new List<GrantGroupCapabilitySelectionDto>(),
            ManualGrantKeys = manualGrantKeys,
            DerivedGrantKeys = derivedGrantKeys.OrderBy(k => k).ToList(),
            TemplateSourceKey = templateSourceKey,
            TemplateSourceVersion = templateSourceVersion,
            HasCapabilityMetadata = hasCapabilityMetadata,
            IsCustomized = isCustomized
        };
    }

    /// <summary>FAZA 2 Part F — "customized" znači: grupa nema stabilnu template provenance, ima Manual Advanced
    /// grantove, ILI njeni trenutni capability odabiri/compatibility grantovi ODSTUPAJU od TOČNO ONE verzije
    /// predloška zabilježene u provenance-u (ne najnovije — usporedba je uvijek prema zabilježenoj verziji).
    /// FAZA 3 (v2 template-upgrade) — promovirano iz private u internal da GrantGroupTemplateUpgradeService (isti
    /// Infrastructure assembly) ponovno iskoristi identičnu drift-logiku umjesto duplikacije (vidi
    /// GetUpgradeStatus.isCustomized) — Core ne smije referencirati GrantGroupCapabilitySnapshot (EF entitet), zato
    /// nije izloženo preko IGrantGroupCapabilityAuthoringService sučelja.</summary>
    internal async Task<bool> ComputeIsCustomized(string templateKey, int? templateVersion, List<GrantGroupCapabilitySnapshot> snapshots, HashSet<string> currentTemplateCompatibilitySet, List<string> manualGrantKeys)
    {
        if (templateKey == null || !templateVersion.HasValue)
            return true;

        if (manualGrantKeys.Count > 0)
            return true;

        if (snapshots.Any(s => s.SourceTemplateKey != templateKey || s.SourceTemplateVersion != templateVersion))
            return true;

        DefaultRoleTemplate template = await _defaultRoleTemplateHandler.GetByKey(templateKey, templateVersion);
        if (template == null)
            return true;

        HashSet<(Guid CapabilityDefinitionId, CapabilitySelectedScope Scope)> templateSelections = template.Capabilities
            .Select(c => (c.CapabilityDefinitionId, c.SelectedScope))
            .ToHashSet();
        HashSet<(Guid CapabilityDefinitionId, CapabilitySelectedScope Scope)> currentSelections = snapshots
            .Select(s => (s.CapabilityDefinitionId, s.SelectedScope))
            .ToHashSet();
        if (!templateSelections.SetEquals(currentSelections))
            return true;

        HashSet<string> templateCompatibilitySet = template.CompatibilityGrants.Select(g => g.GrantKey).ToHashSet();
        return !templateCompatibilitySet.SetEquals(currentTemplateCompatibilitySet);
    }

    /// <summary>FAZA 2 Part C — svaka selekcija: capability postoji, verzija postoji, verzija je aktivna
    /// (dostupna za autorstvo — deprecated verzije se ne mogu (ponovno) odabrati), opseg je legalan za
    /// CapabilityDefinition.ScopeModel, bez duplikata po ključu (ni po ključu+verziji).</summary>
    private async Task<List<TemplateCapabilitySelection>> ResolveAndValidateSelections(List<GrantGroupCapabilitySelectionRequest> requests)
    {
        List<TemplateCapabilitySelection> result = new();
        HashSet<string> seenKeys = new();

        foreach (GrantGroupCapabilitySelectionRequest request in requests)
        {
            if (!seenKeys.Add(request.CapabilityKey))
                throw new BusinessRuleException(ErrorCodes.DuplicateCapabilitySelection, $"Capability '{request.CapabilityKey}' je odabrana više puta.");

            CapabilityDefinition definition = await _capabilityDefinitionHandler.GetByKey(request.CapabilityKey, request.CapabilityVersion);
            if (definition == null)
            {
                CapabilityDefinition anyVersion = await _capabilityDefinitionHandler.GetByKey(request.CapabilityKey, null);
                if (anyVersion == null)
                    throw new BusinessRuleException(ErrorCodes.CapabilityUnknown, $"Nepoznata capability '{request.CapabilityKey}'.");

                throw new BusinessRuleException(ErrorCodes.CapabilityVersionNotFound, $"Capability '{request.CapabilityKey}' verzija {request.CapabilityVersion} ne postoji.");
            }

            if (!definition.IsActive)
                throw new BusinessRuleException(ErrorCodes.CapabilityVersionNotAvailable, $"Capability '{request.CapabilityKey}' v{request.CapabilityVersion} nije dostupna za autorstvo (deprecated).");

            ValidateScopeLegal(definition.ScopeModel, request.SelectedScope, request.CapabilityKey);

            result.Add(new TemplateCapabilitySelection(
                definition.Id.GetValueOrDefault(),
                definition.Key,
                definition.Version,
                definition.ScopeModel,
                definition.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role)).ToList(),
                request.SelectedScope));
        }

        return result;
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

    /// <summary>FAZA 2 Part C/G — svaki ključ mora postojati u katalogu, dedupe, i (MVP no-dual-provenance pravilo)
    /// odbij ako je ključ već proizveden odabranim capability-jima ILI sačuvanom template-compatibility provenance —
    /// preferirano odbijanje umjesto tihe normalizacije jer izlaže klijentski bug (vidi ErrorCodes.GrantAlreadyCapabilityDerived).</summary>
    private static HashSet<string> ValidateManualGrantKeys(List<string> manualGrantKeys, HashSet<string> capabilityDerivedSet, HashSet<string> templateCompatibilitySet)
    {
        HashSet<string> distinct = manualGrantKeys.Distinct().ToHashSet();

        List<string> unknown = distinct.Where(key => !ValidGrantKeys.Contains(key)).ToList();
        if (unknown.Count > 0)
            throw new ValidationAppException(ErrorCodes.GrantKeyUnknown, $"Nepoznati grant-ključevi: {string.Join(", ", unknown)}.");

        List<string> alreadyDerived = distinct.Where(key => capabilityDerivedSet.Contains(key) || templateCompatibilitySet.Contains(key)).ToList();
        if (alreadyDerived.Count > 0)
            throw new BusinessRuleException(ErrorCodes.GrantAlreadyCapabilityDerived, $"Grant(ovi) već proizlaze iz odabranih capability-ja/predloška, ne mogu se ručno dodati: {string.Join(", ", alreadyDerived)}.");

        return distinct;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationAppException("Naziv grant-grupe je obavezan.");
    }

    private HashSet<string> MaterializeAll(List<TemplateCapabilitySelection> selections)
    {
        HashSet<string> result = new();
        foreach (TemplateCapabilitySelection selection in selections)
            result.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));
        return result;
    }

    private static GrantGroupDto ToGrantGroupDto(GrantGroup group)
    {
        return new GrantGroupDto
        {
            Id = group.Id.GetValueOrDefault(),
            Name = group.Name,
            Grants = group.Grants.Select(g => g.GrantKey).OrderBy(k => k).ToList(),
            AssignedUserCount = group.UserGrantGroups.Count,
            CreatedAt = group.CreatedAt,
            UpdatedAt = group.UpdatedAt
        };
    }
}
