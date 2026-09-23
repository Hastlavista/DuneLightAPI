using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.DTOs.Diagnostics;

/// <summary>Ozbiljnost jednog diagnostic nalaza — informativna klasifikacija, ne blokira ništa (vidi
/// IGrantDiagnosticsService — read-only, platform-diagnostika, ne runtime autorizacija).</summary>
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>Vrsta diagnostic nalaza — vidi FAZA 1 Part F za puni popis kategorija koje servis mora pokrivati.</summary>
public enum DiagnosticCategory
{
    /// <summary>Grant postoji u Grants.Catalog, ali ga nijedan RequireGrant/RequireGrantOrAssignedCompany na
    /// otkrivenom endpointu ne referencira.</summary>
    UnusedCatalogGrant,

    /// <summary>RequireGrant/RequireGrantOrAssignedCompany referencira string koji ne postoji u Grants.Catalog —
    /// vjerojatno typo ili zaboravljeno dodavanje u katalog.</summary>
    UndefinedGrantReferenced,

    /// <summary>Grant nije prisutan u nijednoj default GrantGroup-i (Admin/Trener/Recepcija). Napomena: dok je
    /// Admin == cijeli Grants.Catalog (vidi DefaultGrantGroups), ova kategorija je strukturno prazna za Admin;
    /// ostaje korisna ako se Admin ikad promijeni u kurirani popis.</summary>
    GrantMissingFromDefaultRoles,

    /// <summary>Default GrantGroup definicija (Admin/Trener/Recepcija) referencira grant-ključ koji ne postoji
    /// u Grants.Catalog — signal da je katalog izmijenjen bez ažuriranja DefaultGrantGroups.</summary>
    DefaultRoleGrantMissingFromCatalog,

    /// <summary>Isti grant-ključ pojavljuje se više puta u Grants.Catalog listi.</summary>
    DuplicateGrantKey,

    /// <summary>Default-role drift kod postojeće organizacije — vidi DefaultGrantGroupDriftChecker. Postojeće
    /// organizacije se NE mijenjaju automatski (FAZA 1 Part D), ovo je samo izvještaj.</summary>
    DefaultRoleDrift,

    /// <summary>FAZA 1 Part S — raw grant iz Grants.Catalog kojeg NIJEDNA aktivna CapabilityDefinition ne
    /// pokriva (ni kao Primary* ni kao MandatorySupporting). Nije nužno greška (neki grantovi su namjerno
    /// Owner/Advanced-only), ali vrijedi periodično pregledati.</summary>
    RawGrantNotCoveredByCapability,

    /// <summary>CapabilityDefinitionGrant referencira grant-ključ koji ne postoji u Grants.Catalog.</summary>
    CapabilityReferencesUnknownGrant,

    /// <summary>DefaultRoleTemplateCapability referencira CapabilityDefinition verziju koja je deprecated
    /// (DeprecatedAt != null) dok je predložak i dalje aktivan — signal da predložak treba migrirati na noviju verziju.</summary>
    TemplateReferencesInvalidCapabilityVersion,

    /// <summary>DefaultRoleTemplateGrant referencira grant-ključ koji ne postoji u Grants.Catalog.</summary>
    TemplateGrantReferencesUnknownGrant,

    /// <summary>Drift između GrantGroupGrant i onoga što bi njeni GrantGroupCapabilitySnapshot+GrantGroupTemplateGrant
    /// zapisi trebali materijalizirati — precizniji od DefaultRoleDrift jer koristi stabilnu snapshot poveznicu
    /// umjesto Name-only podudaranja (vidi FAZA 1 Part S).</summary>
    SnapshotBasedTemplateDrift
}

/// <summary>Jedan diagnostic nalaz. Details nosi dodatne stringove (npr. popis pogođenih grant-ključeva ili ruta)
/// — strukturirano polje umjesto ugrađivanja svega u Message, da alati/CI mogu strojno obraditi nalaze.</summary>
public record GrantDiagnosticFinding(
    DiagnosticCategory Category,
    DiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<string> Details);

/// <summary>Jedan otkriveni HTTP endpoint i njegovi grant-zahtjevi, dobiveno reflection/action-descriptor
/// inspekcijom (vidi IEndpointGrantMetadataProvider). Isključivo diagnostika — ne koristi se za runtime odluke.</summary>
public record EndpointGrantMetadata(
    string Controller,
    string Action,
    string HttpMethod,
    string Route,
    IReadOnlyList<string> RequiredGrants,
    bool RequireGrantOrAssignedCompany);

/// <summary>Par own/all grantova unutar istog modula, otkriven po konvenciji imenovanja u Grants.Catalog
/// (isti prefiks, ".own" i ".all" nastavci) — npr. appointments.write.own/appointments.write.all.</summary>
public record OwnAllGrantPair(string Module, string OwnKey, string AllKey);

/// <summary>Rezultat usporedbe jedne postojeće GrantGroup-e (kod postojeće organizacije) naspram njoj
/// pripadajućeg default predloška, ako je prepoznatljiv po Name (vidi DefaultGrantGroupDriftChecker i njegovo
/// ograničenje identiteta).</summary>
public record DefaultRoleDriftEntry(
    string TemplateKey,
    string TemplateDisplayName,
    Guid OrganizationId,
    Guid GrantGroupId,
    IReadOnlyList<string> MissingGrants,
    IReadOnlyList<string> ExtraGrants,
    bool IsExactMatch);

/// <summary>Rezultat usporedbe jedne GrantGroup-e naspram onoga što njeni snapshot/provenance zapisi trebaju
/// materijalizirati — vidi DiagnosticCategory.SnapshotBasedTemplateDrift.</summary>
public record SnapshotBasedDriftEntry(
    Guid OrganizationId,
    Guid GrantGroupId,
    string SourceTemplateKey,
    int? SourceTemplateVersion,
    IReadOnlyList<string> MissingGrants,
    IReadOnlyList<string> ExtraGrants,
    bool IsExactMatch);

/// <summary>Puni izvještaj generiran od IGrantDiagnosticsService.GenerateReport() — read-only snapshot, ništa
/// ne mijenja u bazi niti u runtime autorizaciji (vidi FAZA 1 Part F/H).</summary>
public record GrantDiagnosticsReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<EndpointGrantMetadata> Endpoints,
    IReadOnlyList<GrantDiagnosticFinding> Findings,
    IReadOnlyList<OwnAllGrantPair> OwnAllPairs,
    IReadOnlyList<DefaultRoleDriftEntry> DefaultRoleDrift,
    IReadOnlyList<SnapshotBasedDriftEntry> SnapshotDrift,
    IReadOnlyList<string> KnownLimitations);
