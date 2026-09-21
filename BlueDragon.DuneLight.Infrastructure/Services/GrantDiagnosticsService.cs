using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Diagnostics;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IGrantDiagnosticsService — read-only platform diagnostika, ne runtime autorizacija, ne mijenja ništa.
/// </summary>
public class GrantDiagnosticsService : IGrantDiagnosticsService
{
    private readonly IEndpointGrantMetadataProvider _endpointGrantMetadataProvider;
    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly ICapabilityDefinitionHandler _capabilityDefinitionHandler;
    private readonly IDefaultRoleTemplateHandler _defaultRoleTemplateHandler;
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;

    public GrantDiagnosticsService(
        IEndpointGrantMetadataProvider endpointGrantMetadataProvider,
        IGrantGroupHandler grantGroupHandler,
        ICapabilityDefinitionHandler capabilityDefinitionHandler,
        IDefaultRoleTemplateHandler defaultRoleTemplateHandler,
        ICapabilityMaterializationService capabilityMaterializationService)
    {
        _endpointGrantMetadataProvider = endpointGrantMetadataProvider;
        _grantGroupHandler = grantGroupHandler;
        _capabilityDefinitionHandler = capabilityDefinitionHandler;
        _defaultRoleTemplateHandler = defaultRoleTemplateHandler;
        _capabilityMaterializationService = capabilityMaterializationService;
    }

    public async Task<GrantDiagnosticsReport> GenerateReport()
    {
        List<GrantDiagnosticFinding> findings = new();

        List<EndpointGrantMetadata> endpoints = _endpointGrantMetadataProvider.GetEndpointGrantMetadata();
        HashSet<string> catalogKeys = Grants.Catalog.Select(g => g.Key).ToHashSet();
        HashSet<string> referencedGrants = endpoints.SelectMany(e => e.RequiredGrants).ToHashSet();
        HashSet<string> defaultRoleGrants = DefaultGrantGroups.All.SelectMany(d => d.Grants).ToHashSet();

        AddDuplicateGrantKeyFindings(findings);
        AddUnusedCatalogGrantFindings(findings, catalogKeys, referencedGrants);
        AddUndefinedGrantReferencedFindings(findings, catalogKeys, referencedGrants);
        AddGrantMissingFromDefaultRolesFindings(findings, catalogKeys, defaultRoleGrants);
        AddDefaultRoleGrantMissingFromCatalogFindings(findings, catalogKeys);
        AddOwnerOnlySurfaceFindings(findings, endpoints);

        List<OwnAllGrantPair> ownAllPairs = DetectOwnAllPairs();
        List<DefaultRoleDriftEntry> defaultRoleDrift = await DetectDefaultRoleDrift(findings);

        List<CapabilityDefinition> allCapabilityVersions = await _capabilityDefinitionHandler.GetAllForDiagnostics();
        List<DefaultRoleTemplate> allTemplateVersions = await _defaultRoleTemplateHandler.GetAllForDiagnostics();
        AddCapabilityReferencesUnknownGrantFindings(findings, catalogKeys, allCapabilityVersions);
        AddRawGrantNotCoveredByCapabilityFindings(findings, catalogKeys, allCapabilityVersions);
        AddTemplateReferencesInvalidCapabilityVersionFindings(findings, allTemplateVersions);
        AddTemplateGrantReferencesUnknownGrantFindings(findings, catalogKeys, allTemplateVersions);
        List<SnapshotBasedDriftEntry> snapshotDrift = await DetectSnapshotBasedDrift(findings);

        List<string> limitations = new()
        {
            "Prepoznavanje default-role predloška (Admin/Trener/Recepcija) kod postojećih organizacija oslanja se ISKLJUČIVO na GrantGroup.Name — grant_groups tablica nema stupac za stabilan template-id u ovoj fazi. Preimenovana default grupa prestaje biti prepoznata, a korisnička grupa slučajno nazvana identično (npr. 'Admin') bude pogrešno klasificirana kao predložak. SnapshotBasedTemplateDrift (ispod) rješava ovo ZA grupe koje već imaju snapshot metapodatke (nastale nakon capability sustava ili backfillane pri exact-match migraciji) — DefaultRoleDrift (Name-only) ostaje kao fallback za grupe bez njih.",
            "GrantMissingFromDefaultRoles je strukturno uvijek prazna kategorija dok je Admin definiran kao CIJELI Grants.Catalog (vidi DefaultGrantGroups.AdminGrants) — Admin po definiciji pokriva svaki grant. Kategorija je zadržana radi buduće promjene (ako Admin ikad postane kuriran popis).",
            "DefaultRoleDrift i SnapshotBasedTemplateDrift presijecaju SVE organizacije (cross-tenant upit) — namjerno, jer je ovo platform/development dijagnostika, a ne tenant runtime funkcionalnost. Ne smije se koristiti kao osnova za tenant-facing odgovor.",
            "Endpoint metapodaci dolaze iz IActionDescriptorCollectionProvider u trenutku poziva — pokrivaju samo kontrolere registrirane u trenutnom API hostu, ne uključuju eventualne minimal-API endpointove (trenutno ih nema).",
            "RawGrantNotCoveredByCapability/CapabilityReferencesUnknownGrant/TemplateGrantReferencesUnknownGrant presijecaju SVE verzije (uklj. deprecated) — jedna stara, deprecated verzija koja referencira nešto zastarjelo ne bi trebala paničariti, ali je zadržano vidljivo dok se ne pokaže potreba filtrirati na is_active."
        };

        return new GrantDiagnosticsReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            Endpoints: endpoints,
            Findings: findings.OrderByDescending(f => f.Severity).ToList(),
            OwnAllPairs: ownAllPairs,
            DefaultRoleDrift: defaultRoleDrift,
            SnapshotDrift: snapshotDrift,
            KnownLimitations: limitations);
    }

    private static void AddCapabilityReferencesUnknownGrantFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, List<CapabilityDefinition> allCapabilityVersions)
    {
        List<string> unknown = allCapabilityVersions
            .SelectMany(c => c.Grants.Where(g => !catalogKeys.Contains(g.GrantKey)).Select(g => $"{c.Key} v{c.Version}: {g.GrantKey}"))
            .OrderBy(e => e)
            .ToList();

        if (unknown.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.CapabilityReferencesUnknownGrant,
                DiagnosticSeverity.Critical,
                "CapabilityDefinitionGrant referencira grant-ključ koji ne postoji u Grants.Catalog.",
                unknown));
    }

    private static void AddRawGrantNotCoveredByCapabilityFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, List<CapabilityDefinition> allCapabilityVersions)
    {
        HashSet<string> coveredByAnyCapability = allCapabilityVersions
            .Where(c => c.IsActive)
            .SelectMany(c => c.Grants.Select(g => g.GrantKey))
            .ToHashSet();

        List<string> uncovered = catalogKeys.Except(coveredByAnyCapability).OrderBy(k => k).ToList();
        if (uncovered.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.RawGrantNotCoveredByCapability,
                DiagnosticSeverity.Info,
                "Grantovi iz Grants.Catalog koje nijedna AKTIVNA CapabilityDefinition ne pokriva — mogu i dalje biti dodijeljeni ručno (Advanced), ovo nije nužno greška.",
                uncovered));
    }

    private static void AddTemplateReferencesInvalidCapabilityVersionFindings(List<GrantDiagnosticFinding> findings, List<DefaultRoleTemplate> allTemplateVersions)
    {
        List<string> invalid = allTemplateVersions
            .Where(t => t.IsActive)
            .SelectMany(t => t.Capabilities
                .Where(c => c.CapabilityDefinition == null || c.CapabilityDefinition.DeprecatedAt != null)
                .Select(c => $"{t.Key} v{t.Version} -> {c.CapabilityDefinition?.Key ?? "?"} v{c.CapabilityDefinition?.Version.ToString() ?? "?"}"))
            .OrderBy(e => e)
            .ToList();

        if (invalid.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.TemplateReferencesInvalidCapabilityVersion,
                DiagnosticSeverity.Warning,
                "Aktivan DefaultRoleTemplate referencira deprecated/nepostojeću CapabilityDefinition verziju.",
                invalid));
    }

    private static void AddTemplateGrantReferencesUnknownGrantFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, List<DefaultRoleTemplate> allTemplateVersions)
    {
        List<string> unknown = allTemplateVersions
            .SelectMany(t => t.CompatibilityGrants
                .Where(g => !catalogKeys.Contains(g.GrantKey))
                .Select(g => $"{t.Key} v{t.Version}: {g.GrantKey}"))
            .OrderBy(e => e)
            .ToList();

        if (unknown.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.TemplateGrantReferencesUnknownGrant,
                DiagnosticSeverity.Critical,
                "DefaultRoleTemplateGrant (compatibility-extra) referencira grant-ključ koji ne postoji u Grants.Catalog.",
                unknown));
    }

    private async Task<List<SnapshotBasedDriftEntry>> DetectSnapshotBasedDrift(List<GrantDiagnosticFinding> findings)
    {
        List<GrantGroupCapabilitySnapshot> allSnapshots = await _grantGroupHandler.GetAllCapabilitySnapshotsForDiagnostics();
        List<GrantGroupTemplateGrant> allTemplateGrants = await _grantGroupHandler.GetAllTemplateGrantProvenanceForDiagnostics();
        List<GrantGroup> allGroups = await _grantGroupHandler.GetAllAcrossOrganizationsForDiagnostics();

        Dictionary<Guid, GrantGroup> groupsById = allGroups.ToDictionary(g => g.Id.GetValueOrDefault());
        List<SnapshotBasedDriftEntry> entries = new();
        List<string> driftedDescriptions = new();

        foreach (var groupSnapshots in allSnapshots.GroupBy(s => s.GrantGroupId))
        {
            if (!groupsById.TryGetValue(groupSnapshots.Key, out GrantGroup group))
                continue;

            HashSet<string> expected = new();
            foreach (GrantGroupCapabilitySnapshot snapshot in groupSnapshots)
            {
                List<CapabilityGrantRoleEntry> grantEntries = snapshot.CapabilityDefinition.Grants
                    .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role))
                    .ToList();
                expected.UnionWith(_capabilityMaterializationService.Materialize(snapshot.CapabilityDefinition.ScopeModel, snapshot.SelectedScope, grantEntries));
            }

            expected.UnionWith(allTemplateGrants.Where(g => g.GrantGroupId == groupSnapshots.Key).Select(g => g.GrantKey));

            HashSet<string> actual = group.Grants.Select(g => g.GrantKey).ToHashSet();
            List<string> missing = expected.Except(actual).OrderBy(k => k).ToList();
            List<string> extra = actual.Except(expected).OrderBy(k => k).ToList();
            bool isExactMatch = missing.Count == 0 && extra.Count == 0;

            GrantGroupCapabilitySnapshot first = groupSnapshots.First();
            entries.Add(new SnapshotBasedDriftEntry(
                group.OrganizationId, groupSnapshots.Key, first.SourceTemplateKey, first.SourceTemplateVersion, missing, extra, isExactMatch));

            // Napomena: "extra" ovdje NIJE nužno drift — ManualAdvancedSet (ručno dodani grantovi preko Advanced
            // editora) je legitiman i po dizajnu ostaje izvan capability/template pokrivenosti (vidi FAZA 1 Part J).
            // Prijavljujemo samo missing kao stvarni signal (snapshot tvrdi da je nešto materijalizirano, a nije).
            if (missing.Count > 0)
                driftedDescriptions.Add($"org={group.OrganizationId} group={group.Id} template={first.SourceTemplateKey} v{first.SourceTemplateVersion} missing=[{string.Join(",", missing)}]");
        }

        if (driftedDescriptions.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.SnapshotBasedTemplateDrift,
                DiagnosticSeverity.Warning,
                "GrantGroup ima capability/template snapshot metapodatke koji tvrde da bi određeni raw grant trebao postojati, ali ga trenutni GrantGroupGrant skup nema.",
                driftedDescriptions));

        return entries;
    }

    private static void AddDuplicateGrantKeyFindings(List<GrantDiagnosticFinding> findings)
    {
        List<string> duplicates = Grants.Catalog
            .GroupBy(g => g.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.DuplicateGrantKey,
                DiagnosticSeverity.Critical,
                "Isti grant-ključ pojavljuje se više puta u Grants.Catalog.",
                duplicates));
    }

    private static void AddUnusedCatalogGrantFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, HashSet<string> referencedGrants)
    {
        List<string> unused = catalogKeys.Except(referencedGrants).OrderBy(k => k).ToList();
        if (unused.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.UnusedCatalogGrant,
                DiagnosticSeverity.Info,
                "Grantovi definirani u Grants.Catalog, ali ih nijedan RequireGrant/RequireGrantOrAssignedCompany endpoint trenutno ne referencira.",
                unused));
    }

    private static void AddUndefinedGrantReferencedFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, HashSet<string> referencedGrants)
    {
        List<string> undefined = referencedGrants.Except(catalogKeys).OrderBy(k => k).ToList();
        if (undefined.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.UndefinedGrantReferenced,
                DiagnosticSeverity.Critical,
                "RequireGrant/RequireGrantOrAssignedCompany referencira grant-ključ koji ne postoji u Grants.Catalog (vjerojatno typo).",
                undefined));
    }

    private static void AddGrantMissingFromDefaultRolesFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys, HashSet<string> defaultRoleGrants)
    {
        List<string> missing = catalogKeys.Except(defaultRoleGrants).OrderBy(k => k).ToList();
        if (missing.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.GrantMissingFromDefaultRoles,
                DiagnosticSeverity.Info,
                "Grantovi koji nisu prisutni u nijednoj default GrantGroup-i (Admin/Trener/Recepcija). Neki grantovi su namjerno Owner/custom-only — ovo NIJE nužno greška.",
                missing));
    }

    private static void AddDefaultRoleGrantMissingFromCatalogFindings(List<GrantDiagnosticFinding> findings, HashSet<string> catalogKeys)
    {
        foreach (DefaultGrantGroupDefinition definition in DefaultGrantGroups.All)
        {
            List<string> unknown = definition.Grants.Where(k => !catalogKeys.Contains(k)).OrderBy(k => k).ToList();
            if (unknown.Count > 0)
                findings.Add(new GrantDiagnosticFinding(
                    DiagnosticCategory.DefaultRoleGrantMissingFromCatalog,
                    DiagnosticSeverity.Critical,
                    $"Default predložak '{definition.DisplayName}' referencira grant-ključ(eve) koji ne postoje u Grants.Catalog.",
                    unknown));
        }
    }

    private static void AddOwnerOnlySurfaceFindings(List<GrantDiagnosticFinding> findings, List<EndpointGrantMetadata> endpoints)
    {
        List<string> ownerOnlyRoutes = endpoints
            .Where(e => e.RequireOwner)
            .Select(e => $"{e.HttpMethod} /{e.Route} ({e.Controller}.{e.Action})")
            .OrderBy(r => r)
            .ToList();

        if (ownerOnlyRoutes.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.OwnerOnlySurface,
                DiagnosticSeverity.Info,
                "Endpointi zaštićeni isključivo s [RequireOwner] — potpuno zaobilaze grant sustav, vrijedi periodično provjeriti da se opseg ne širi bez razloga.",
                ownerOnlyRoutes));
    }

    private static List<OwnAllGrantPair> DetectOwnAllPairs()
    {
        Dictionary<string, GrantDefinition> byKey = Grants.Catalog.ToDictionary(g => g.Key);
        List<OwnAllGrantPair> pairs = new();

        foreach (GrantDefinition definition in Grants.Catalog)
        {
            if (!definition.Key.EndsWith(".own", StringComparison.Ordinal))
                continue;

            string allKey = definition.Key[..^".own".Length] + ".all";
            if (byKey.ContainsKey(allKey))
                pairs.Add(new OwnAllGrantPair(definition.Module, definition.Key, allKey));
        }

        return pairs.OrderBy(p => p.Module).ThenBy(p => p.OwnKey).ToList();
    }

    private async Task<List<DefaultRoleDriftEntry>> DetectDefaultRoleDrift(List<GrantDiagnosticFinding> findings)
    {
        Dictionary<string, DefaultGrantGroupDefinition> byDisplayName = DefaultGrantGroups.All
            .ToDictionary(d => d.DisplayName);

        List<GrantGroup> allGroups = await _grantGroupHandler.GetAllAcrossOrganizationsForDiagnostics();
        List<DefaultRoleDriftEntry> entries = new();
        List<string> driftedDescriptions = new();

        foreach (GrantGroup group in allGroups)
        {
            if (!byDisplayName.TryGetValue(group.Name, out DefaultGrantGroupDefinition definition))
                continue;

            GrantGroupDriftResult drift = DefaultGrantGroupDriftChecker.Compare(definition, group.Grants.Select(g => g.GrantKey));

            entries.Add(new DefaultRoleDriftEntry(
                drift.TemplateKey,
                drift.TemplateDisplayName,
                group.OrganizationId,
                group.Id.GetValueOrDefault(),
                drift.MissingGrants,
                drift.ExtraGrants,
                drift.IsExactMatch));

            if (!drift.IsExactMatch)
                driftedDescriptions.Add(
                    $"org={group.OrganizationId} group={group.Id} template={definition.DisplayName} missing=[{string.Join(",", drift.MissingGrants)}] extra=[{string.Join(",", drift.ExtraGrants)}]");
        }

        if (driftedDescriptions.Count > 0)
            findings.Add(new GrantDiagnosticFinding(
                DiagnosticCategory.DefaultRoleDrift,
                DiagnosticSeverity.Warning,
                "Postojeće organizacije čija GrantGroup (prepoznata po Name) odstupa od trenutnog default predloška — namjerno se ne ispravlja automatski (vidi FAZA 1 Part D).",
                driftedDescriptions));

        return entries;
    }
}
