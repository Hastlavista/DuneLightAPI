using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Diagnostics;
using BlueDragon.DuneLight.Core.Interfaces.Diagnostics;
using BlueDragon.DuneLight.Core.Shared;
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

    public GrantDiagnosticsService(IEndpointGrantMetadataProvider endpointGrantMetadataProvider, IGrantGroupHandler grantGroupHandler)
    {
        _endpointGrantMetadataProvider = endpointGrantMetadataProvider;
        _grantGroupHandler = grantGroupHandler;
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

        List<string> limitations = new()
        {
            "Prepoznavanje default-role predloška (Admin/Trener/Recepcija) kod postojećih organizacija oslanja se ISKLJUČIVO na GrantGroup.Name — grant_groups tablica nema stupac za stabilan template-id u ovoj fazi. Preimenovana default grupa prestaje biti prepoznata, a korisnička grupa slučajno nazvana identično (npr. 'Admin') bude pogrešno klasificirana kao predložak. Rješava se kad CapabilityDefinition/template-id stigne u kasnijoj fazi.",
            "GrantMissingFromDefaultRoles je strukturno uvijek prazna kategorija dok je Admin definiran kao CIJELI Grants.Catalog (vidi DefaultGrantGroups.AdminGrants) — Admin po definiciji pokriva svaki grant. Kategorija je zadržana radi buduće promjene (ako Admin ikad postane kuriran popis).",
            "DefaultRoleDrift presijeca SVE organizacije (cross-tenant upit) — namjerno, jer je ovo platform/development dijagnostika, a ne tenant runtime funkcionalnost. Ne smije se koristiti kao osnova za tenant-facing odgovor.",
            "Endpoint metapodaci dolaze iz IActionDescriptorCollectionProvider u trenutku poziva — pokrivaju samo kontrolere registrirane u trenutnom API hostu, ne uključuju eventualne minimal-API endpointove (trenutno ih nema)."
        };

        return new GrantDiagnosticsReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            Endpoints: endpoints,
            Findings: findings.OrderByDescending(f => f.Severity).ToList(),
            OwnAllPairs: ownAllPairs,
            DefaultRoleDrift: defaultRoleDrift,
            KnownLimitations: limitations);
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
