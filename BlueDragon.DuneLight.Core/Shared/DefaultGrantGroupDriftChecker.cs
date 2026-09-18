using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Read-only usporedba očekivanih grantova default predloška (DefaultGrantGroups) naspram stvarnih grantova
/// postojeće GrantGroup-e u bazi. NE mijenja ništa — samo izvještava (missing/extra/exact match). Namijenjeno
/// diagnostici (vidi IGrantDiagnosticsService), ne runtime autorizaciji.
///
/// OGRANIČENJE IDENTITETA: grant_groups tablica nema stupac koji bilježi "ovo je Admin predložak" — jedina
/// poveznica je Name (vidi DefaultGrantGroupDefinition.DisplayName). Zato ova usporedba NE smije se pozivati na
/// slučajnu grupu koja se samo zove "Admin" bez dodatne potvrde s pozivnog mjesta (vidi
/// GrantDiagnosticsService — filtrira po točnom nazivu prije poziva). Korisnik može preimenovati default grupu
/// (izgubi se prepoznavanje) ili nazvati vlastitu prilagođenu grupu "Admin" (lažno prepoznata kao predložak) —
/// ovo je poznato ograničenje ove faze, riješit će se stabilnim ID-em kad dođe CapabilityDefinition/template stupac.
/// </summary>
public static class DefaultGrantGroupDriftChecker
{
    public static GrantGroupDriftResult Compare(DefaultGrantGroupDefinition definition, IEnumerable<string> actualGrantKeys)
    {
        HashSet<string> actual = actualGrantKeys.ToHashSet();
        HashSet<string> expected = definition.Grants.ToHashSet();

        List<string> missing = expected.Except(actual).OrderBy(k => k).ToList();
        List<string> extra = actual.Except(expected).OrderBy(k => k).ToList();

        return new GrantGroupDriftResult(
            definition.Key,
            definition.DisplayName,
            missing,
            extra,
            IsExactMatch: missing.Count == 0 && extra.Count == 0);
    }
}

/// <summary>Rezultat usporedbe jedne postojeće GrantGroup-e naspram njoj pridruženog default predloška (po Name).</summary>
public record GrantGroupDriftResult(
    string TemplateKey,
    string TemplateDisplayName,
    IReadOnlyList<string> MissingGrants,
    IReadOnlyList<string> ExtraGrants,
    bool IsExactMatch);
