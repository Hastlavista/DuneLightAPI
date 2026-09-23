using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Services;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>FAZA 3 — vidi IGrantGroupTemplateUpgradeService. Tanak orkestracijski sloj: puni
/// TemplateUpgradePlanningInput iz baze (preko IGrantGroupHandler/IDefaultRoleTemplateHandler), poziva
/// TemplateUpgradePlanner.Plan (ISTI poziv iz GetDiff/Preview/Apply — nikad duplicirana logika), mapira rezultat u
/// DTO-e, i za Apply otvara jednu IUnitOfWork transakciju koja atomski piše snapshot/provenance/raw-grant +
/// audit log preko GrantGroupHandler.ApplyResolvedSelections.</summary>
public class GrantGroupTemplateUpgradeService : IGrantGroupTemplateUpgradeService
{
    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly IDefaultRoleTemplateHandler _defaultRoleTemplateHandler;
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;
    private readonly GrantGroupCapabilityAuthoringService _authoringService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IPermissionAdministrationSafetyService _permissionAdministrationSafetyService;
    private readonly TemplateUpgradePlanner _planner;

    public GrantGroupTemplateUpgradeService(
        IGrantGroupHandler grantGroupHandler,
        IDefaultRoleTemplateHandler defaultRoleTemplateHandler,
        ICapabilityMaterializationService capabilityMaterializationService,
        GrantGroupCapabilityAuthoringService authoringService,
        IUnitOfWorkFactory unitOfWorkFactory,
        IPermissionAdministrationSafetyService permissionAdministrationSafetyService)
    {
        _grantGroupHandler = grantGroupHandler;
        _defaultRoleTemplateHandler = defaultRoleTemplateHandler;
        _capabilityMaterializationService = capabilityMaterializationService;
        _authoringService = authoringService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _permissionAdministrationSafetyService = permissionAdministrationSafetyService;
        _planner = new TemplateUpgradePlanner(capabilityMaterializationService);
    }

    public async Task<GrantGroupTemplateUpgradeStatusDto> GetUpgradeStatus(Guid organizationId, Guid grantGroupId)
    {
        GrantGroup group = await _grantGroupHandler.GetById(organizationId, grantGroupId);
        if (group == null)
            throw new NotFoundAppException("GrantGroup", grantGroupId);

        List<GrantGroupCapabilitySnapshot> snapshots = await _grantGroupHandler.GetCapabilitySnapshots(organizationId, grantGroupId);
        List<GrantGroupTemplateGrant> templateGrants = await _grantGroupHandler.GetTemplateGrantProvenance(organizationId, grantGroupId);

        (string sourceTemplateKey, int? sourceTemplateVersion) = ResolveSourceTemplate(snapshots, templateGrants);

        if (sourceTemplateKey == null || !sourceTemplateVersion.HasValue)
        {
            return new GrantGroupTemplateUpgradeStatusDto
            {
                GrantGroupId = grantGroupId,
                HasUpgrade = false,
                TemplateKey = null,
                CurrentTemplateVersion = null,
                LatestTemplateVersion = null,
                IsCustomized = true
            };
        }

        DefaultRoleTemplate latest = await _defaultRoleTemplateHandler.GetByKey(sourceTemplateKey, null);
        int? latestVersion = latest?.Version;

        // Part B — HasUpgrade je NEOVISAN o IsCustomized (drift nikad ne skriva dostupnost novije verzije).
        bool hasUpgrade = latestVersion.HasValue && latestVersion.Value > sourceTemplateVersion.Value;

        HashSet<string> templateCompatibilitySet = templateGrants.Select(g => g.GrantKey).ToHashSet();
        List<string> manualGrantKeys = this.ComputeManualGrantKeysForCustomizationCheck(group, snapshots, templateCompatibilitySet);

        bool isCustomized = await _authoringService.ComputeIsCustomized(sourceTemplateKey, sourceTemplateVersion, snapshots, templateCompatibilitySet, manualGrantKeys);

        return new GrantGroupTemplateUpgradeStatusDto
        {
            GrantGroupId = grantGroupId,
            HasUpgrade = hasUpgrade,
            TemplateKey = sourceTemplateKey,
            CurrentTemplateVersion = sourceTemplateVersion,
            LatestTemplateVersion = latestVersion,
            IsCustomized = isCustomized
        };
    }

    public async Task<GrantGroupTemplateDiffDto> GetDiff(Guid organizationId, Guid grantGroupId, int targetTemplateVersion)
    {
        (TemplateUpgradePlanningResult result, string sourceTemplateKey, int sourceTemplateVersion, string stateToken) =
            await BuildPlan(organizationId, grantGroupId, targetTemplateVersion, resolutions: new Dictionary<string, ConflictResolution>());

        return new GrantGroupTemplateDiffDto
        {
            CurrentTemplate = new TemplateVersionRefDto(sourceTemplateKey, sourceTemplateVersion),
            TargetTemplate = new TemplateVersionRefDto(sourceTemplateKey, targetTemplateVersion),
            AddedCapabilities = result.AddedCapabilities.Select(ToDto).ToList(),
            RemovedCapabilities = result.RemovedCapabilities.Select(ToDto).ToList(),
            ChangedCapabilities = result.ChangedCapabilities.Select(ToDto).ToList(),
            RawGrantsAdded = result.RawGrantsAdded.Select(ToDto).ToList(),
            RawGrantsRemoved = result.RawGrantsRemoved.Select(ToDto).ToList(),
            RawGrantsUnchanged = result.RawGrantsUnchanged.Select(ToDto).ToList(),
            Conflicts = result.Conflicts.Select(ToDto).ToList(),
            StateToken = stateToken
        };
    }

    public async Task<GrantGroupTemplateUpgradePlanDto> Preview(Guid organizationId, Guid grantGroupId, GrantGroupTemplateUpgradePlanRequest request)
    {
        Dictionary<string, ConflictResolution> resolutions = ParseResolutions(request.Resolutions);

        (TemplateUpgradePlanningResult result, string _, int _, string stateToken) =
            await BuildPlan(organizationId, grantGroupId, request.TargetTemplateVersion, resolutions);

        ValidateStateToken(request.StateToken, stateToken);
        ValidateFullyResolved(result);

        return BuildPlanDto(result, stateToken);
    }

    public async Task<GrantGroupAuthoringDto> Apply(Guid organizationId, Guid userId, Guid grantGroupId, GrantGroupTemplateUpgradePlanRequest request)
    {
        Dictionary<string, ConflictResolution> resolutions = ParseResolutions(request.Resolutions);

        (TemplateUpgradePlanningResult result, string sourceTemplateKey, int sourceTemplateVersion, string stateToken) =
            await BuildPlan(organizationId, grantGroupId, request.TargetTemplateVersion, resolutions);

        ValidateStateToken(request.StateToken, stateToken);
        ValidateFullyResolved(result);

        await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

        await _grantGroupHandler.ApplyResolvedSelections(
            uow,
            grantGroupId,
            result.ResultingCapabilitySelections.Select(ToTemplateCapabilitySelection).ToList(),
            new HashSet<string>(result.ResultingTemplateCompatibilityGrantKeys),
            sourceTemplateKey,
            request.TargetTemplateVersion,
            new HashSet<string>(result.PreservedManualGrantKeys),
            userId);

        uow.Context.GrantGroupTemplateUpgradeAuditLogs.Add(new GrantGroupTemplateUpgradeAuditLog
        {
            GrantGroupId = grantGroupId,
            OrganizationId = organizationId,
            SourceTemplateKey = sourceTemplateKey,
            SourceTemplateVersion = sourceTemplateVersion,
            TargetTemplateKey = sourceTemplateKey,
            TargetTemplateVersion = request.TargetTemplateVersion,
            CapabilityChangesJson = BuildCapabilityChangesJson(result),
            ConflictResolutionsJson = BuildConflictResolutionsJson(resolutions),
            AppliedAt = DateTimeOffset.UtcNow,
            AppliedBy = userId
        });

        // Part F — template upgrade može ukloniti permissions.manage s grupe (npr. novija verzija mijenja
        // selekcije, ili razriješeni konflikt uklanja ručni grant). ApplyResolvedSelections je gore VEĆ upisao
        // novi grant skup, pa provjera ovdje vidi stvarno novo stanje — baca PRIJE commita.
        await _permissionAdministrationSafetyService.EnsureRetainsPermissionAdminInTransaction(uow, organizationId);

        // uow.CommitAsync() radi SaveChangesAsync + transaction commit u jednom koraku (vidi UnitOfWork.CommitAsync)
        // — audit log redak i ApplyResolvedSelections-ove promjene commitaju se ATOMSKI, u istoj transakciji.
        await uow.CommitAsync();

        return await _authoringService.GetAuthoringState(organizationId, grantGroupId);
    }

    private async Task<(TemplateUpgradePlanningResult Result, string SourceTemplateKey, int SourceTemplateVersion, string StateToken)> BuildPlan(
        Guid organizationId, Guid grantGroupId, int targetTemplateVersion, IReadOnlyDictionary<string, ConflictResolution> resolutions)
    {
        GrantGroup group = await _grantGroupHandler.GetById(organizationId, grantGroupId);
        if (group == null)
            throw new NotFoundAppException("GrantGroup", grantGroupId);

        List<GrantGroupCapabilitySnapshot> snapshots = await _grantGroupHandler.GetCapabilitySnapshots(organizationId, grantGroupId);
        List<GrantGroupTemplateGrant> templateGrants = await _grantGroupHandler.GetTemplateGrantProvenance(organizationId, grantGroupId);

        (string sourceTemplateKey, int? sourceTemplateVersionNullable) = ResolveSourceTemplate(snapshots, templateGrants);
        if (sourceTemplateKey == null || !sourceTemplateVersionNullable.HasValue)
            throw new BusinessRuleException(ErrorCodes.GrantGroupHasNoTemplateProvenance, "Ova grant-grupa nema predložak provenance — upgrade nije primjenjiv.");
        int sourceTemplateVersion = sourceTemplateVersionNullable.Value;

        DefaultRoleTemplate baseTemplate = await _defaultRoleTemplateHandler.GetByKey(sourceTemplateKey, sourceTemplateVersion);
        if (baseTemplate == null)
            throw new NotFoundAppException(ErrorCodes.TargetTemplateVersionNotFound, $"Predložak '{sourceTemplateKey}' verzija {sourceTemplateVersion} (BASE) ne postoji.");

        DefaultRoleTemplate targetTemplate = await _defaultRoleTemplateHandler.GetByKey(sourceTemplateKey, targetTemplateVersion);
        if (targetTemplate == null)
            throw new NotFoundAppException(ErrorCodes.TargetTemplateVersionNotFound, $"Predložak '{sourceTemplateKey}' verzija {targetTemplateVersion} (TARGET) ne postoji.");

        Dictionary<Guid, TemplateSelectionInput> baseByDefinitionId = baseTemplate.Capabilities
            .ToDictionary(c => c.CapabilityDefinitionId, c => ToTemplateSelectionInput(c));

        List<CapabilitySnapshotInput> currentSnapshots = snapshots.Select(ToCapabilitySnapshotInput).ToList();
        List<TemplateSelectionInput> targetSelections = targetTemplate.Capabilities.Select(ToTemplateSelectionInput).ToList();

        HashSet<string> currentTemplateCompatibilityGrantKeys = templateGrants.Select(g => g.GrantKey).ToHashSet();
        HashSet<string> targetTemplateCompatibilityGrantKeys = targetTemplate.CompatibilityGrants.Select(g => g.GrantKey).ToHashSet();
        HashSet<string> existingRawGrantKeys = group.Grants.Select(g => g.GrantKey).ToHashSet();

        TemplateUpgradePlanningInput input = new(
            TemplateKey: sourceTemplateKey,
            CurrentTemplateVersion: sourceTemplateVersion,
            TargetTemplateVersion: targetTemplateVersion,
            CurrentSnapshots: currentSnapshots,
            TargetSelections: targetSelections,
            CurrentTemplateCompatibilityGrantKeys: currentTemplateCompatibilityGrantKeys,
            TargetTemplateCompatibilityGrantKeys: targetTemplateCompatibilityGrantKeys,
            ExistingRawGrantKeys: existingRawGrantKeys,
            BaseResolver: id => baseByDefinitionId.TryGetValue(id, out TemplateSelectionInput baseSelection) ? baseSelection : null,
            Resolutions: resolutions);

        TemplateUpgradePlanningResult result = _planner.Plan(input);

        HashSet<string> currentCapabilityDerivedSet = MaterializeAll(currentSnapshots.Select(ToTemplateSelectionInput));
        HashSet<string> manualGrantKeys = new(existingRawGrantKeys);
        manualGrantKeys.ExceptWith(currentCapabilityDerivedSet);
        manualGrantKeys.ExceptWith(currentTemplateCompatibilityGrantKeys);

        string stateToken = TemplateUpgradeStateToken.Compute(
            sourceTemplateKey,
            sourceTemplateVersion,
            snapshots.Select(s => (s.CapabilityDefinitionId, s.SelectedScope.ToString())),
            currentTemplateCompatibilityGrantKeys,
            manualGrantKeys);

        return (result, sourceTemplateKey, sourceTemplateVersion, stateToken);
    }

    private static void ValidateStateToken(string requestToken, string currentToken)
    {
        if (!string.Equals(requestToken, currentToken, StringComparison.Ordinal))
            throw new BusinessRuleException(ErrorCodes.GrantGroupUpgradeStateChanged, "Stanje ove grant-grupe se promijenilo od zadnjeg pregleda — osvježite diff prije nastavka.");
    }

    private static void ValidateFullyResolved(TemplateUpgradePlanningResult result)
    {
        if (!result.IsFullyResolved)
            throw new BusinessRuleException(
                ErrorCodes.GrantGroupUpgradeConflictResolutionRequired,
                "Svi konflikti moraju biti eksplicitno razriješeni prije primjene.",
                new { unresolvedCapabilityKeys = result.UnresolvedConflictCapabilityKeys });
    }

    private static GrantGroupTemplateUpgradePlanDto BuildPlanDto(TemplateUpgradePlanningResult result, string stateToken)
    {
        return new GrantGroupTemplateUpgradePlanDto
        {
            ResultingCapabilitySelections = result.ResultingCapabilitySelections
                .Select(s => new GrantGroupCapabilitySelectionDto(s.CapabilityKey, CapabilityVersionPlaceholder, s.SelectedScope))
                .ToList(),
            PreservedManualGrantKeys = result.PreservedManualGrantKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            ResultingTemplateCompatibilityGrants = result.ResultingTemplateCompatibilityGrantKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            RawGrantsAdded = result.RawGrantsAdded.Select(ToDto).ToList(),
            RawGrantsRemoved = result.RawGrantsRemoved.Select(ToDto).ToList(),
            ConflictsResolved = result.Conflicts.Select(c => c.CapabilityKey).OrderBy(k => k, StringComparer.Ordinal).ToList(),
            StateToken = stateToken
        };
    }

    // GrantGroupCapabilitySelectionDto.CapabilityVersion nije korišten za primjenu (samo prikaz) — planner rezultat
    // ne nosi verziju (Core planer model je namjerno minimalan). 0 je bezopasan placeholder za PREVIEW prikaz;
    // Apply put NIKAD ne koristi ovaj DTO za pisanje (vidi ToTemplateCapabilitySelection ispod, koji verziju
    // uopće ne treba jer je GrantGroupCapabilitySnapshot.CapabilityDefinitionId dovoljan identitet za upis).
    private const int CapabilityVersionPlaceholder = 0;

    private static string BuildCapabilityChangesJson(TemplateUpgradePlanningResult result)
    {
        var payload = new
        {
            added = result.AddedCapabilities.Select(c => new { c.CapabilityKey, c.TargetScope }),
            removed = result.RemovedCapabilities.Select(c => new { c.CapabilityKey, c.CurrentScope }),
            changed = result.ChangedCapabilities.Select(c => new { c.CapabilityKey, c.CurrentScope, c.TargetScope }),
            conflicts = result.Conflicts.Select(c => new { c.CapabilityKey, c.BaseScope, c.CurrentScope, c.TargetScope })
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private static string BuildConflictResolutionsJson(IReadOnlyDictionary<string, ConflictResolution> resolutions)
    {
        return System.Text.Json.JsonSerializer.Serialize(resolutions.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()));
    }

    private static Dictionary<string, ConflictResolution> ParseResolutions(List<ConflictResolutionEntryRequest> requests)
    {
        Dictionary<string, ConflictResolution> result = new();
        foreach (ConflictResolutionEntryRequest entry in requests)
        {
            if (!Enum.TryParse(entry.Resolution, out ConflictResolution parsed))
                throw new ValidationAppException($"Nepoznata vrijednost resolution '{entry.Resolution}' za capability '{entry.CapabilityKey}'.");

            result[entry.CapabilityKey] = parsed;
        }
        return result;
    }

    private static (string TemplateKey, int? TemplateVersion) ResolveSourceTemplate(List<GrantGroupCapabilitySnapshot> snapshots, List<GrantGroupTemplateGrant> templateGrants)
    {
        string templateKey = templateGrants.Select(g => g.SourceTemplateKey).FirstOrDefault()
            ?? snapshots.Select(s => s.SourceTemplateKey).FirstOrDefault(k => k != null);
        int? templateVersion = templateGrants.Select(g => (int?)g.SourceTemplateVersion).FirstOrDefault()
            ?? snapshots.Select(s => s.SourceTemplateVersion).FirstOrDefault(v => v.HasValue);

        return (templateKey, templateVersion);
    }

    private List<string> ComputeManualGrantKeysForCustomizationCheck(GrantGroup group, List<GrantGroupCapabilitySnapshot> snapshots, HashSet<string> templateCompatibilitySet)
    {
        // Isti obrazac kao GrantGroupCapabilityAuthoringService.BuildAuthoringDto.ManualGrantKeys — potrebno SAMO
        // da se ComputeIsCustomized ponaša identično kao authoring-state prikaz (isti "customized" ugovor).
        HashSet<string> capabilityDerivedSet = new();
        foreach (GrantGroupCapabilitySnapshot snapshot in snapshots)
        {
            List<CapabilityGrantRoleEntry> entries = snapshot.CapabilityDefinition.Grants
                .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role))
                .ToList();
            capabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(snapshot.CapabilityDefinition.ScopeModel, snapshot.SelectedScope, entries));
        }

        HashSet<string> derivedGrantKeys = new(capabilityDerivedSet);
        derivedGrantKeys.UnionWith(templateCompatibilitySet);

        return group.Grants.Select(g => g.GrantKey).Where(k => !derivedGrantKeys.Contains(k)).OrderBy(k => k).ToList();
    }

    private HashSet<string> MaterializeAll(IEnumerable<TemplateSelectionInput> selections)
    {
        HashSet<string> result = new();
        foreach (TemplateSelectionInput selection in selections)
            result.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));
        return result;
    }

    private static TemplateSelectionInput ToTemplateSelectionInput(DefaultRoleTemplateCapability c) => new(
        c.CapabilityDefinitionId, c.CapabilityDefinition.Key, c.CapabilityDefinition.ScopeModel, c.SelectedScope,
        c.CapabilityDefinition.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role)).ToList());

    private static TemplateSelectionInput ToTemplateSelectionInput(CapabilitySnapshotInput s) => new(
        s.CapabilityDefinitionId, s.CapabilityKey, s.ScopeModel, s.SelectedScope, s.Grants);

    private static CapabilitySnapshotInput ToCapabilitySnapshotInput(GrantGroupCapabilitySnapshot s) => new(
        s.CapabilityDefinitionId, s.CapabilityDefinition.Key, s.CapabilityDefinition.ScopeModel, s.SelectedScope,
        s.CapabilityDefinition.Grants.Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role)).ToList(),
        s.SourceTemplateKey, s.SourceTemplateVersion);

    private static TemplateCapabilitySelection ToTemplateCapabilitySelection(TemplateSelectionInput s) => new(
        s.CapabilityDefinitionId, s.CapabilityKey, CapabilityVersionPlaceholder, s.ScopeModel, s.Grants, s.SelectedScope);

    private static CapabilityDiffEntryDto ToDto(CapabilityDiffEntry e) => new(e.CapabilityKey, e.CurrentScope, e.TargetScope);

    private static RawGrantChangeDto ToDto(RawGrantChange c) => new(c.GrantKey, c.Source.ToString());

    private static ConflictDto ToDto(UpgradeConflict c) => new(c.CapabilityKey, c.BaseScope, c.CurrentScope, c.TargetScope, c.DefaultResolution.ToString());
}
