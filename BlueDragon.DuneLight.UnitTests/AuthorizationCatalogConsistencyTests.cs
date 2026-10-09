#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueDragon.DuneLight.API.Authorization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// ADR-0023 — neispravne autorizacijske definicije padaju u buildu/testu, ne u runtime dijagnostici. Provjerava
/// statični katalog grantova (Grants.Catalog) i capabilityja (CapabilityCatalog) prema [RequireGrant] /
/// [RequireGrantOrAssignedCompany] atributima svih kontrolera.
/// </summary>
public class AuthorizationCatalogConsistencyTests
{
    /// <summary>
    /// Grantovi koji namjerno ne stoje ni u jednom [RequireGrant]-u nego se provjeravaju u poslovnoj logici. Svaki unos
    /// mora imati razlog; novi grant bez endpointa ne smije ovdje završiti samo da bi test prošao.
    /// </summary>
    private static readonly Dictionary<string, string> CheckedInCodeOnly = new()
    {
        [Grants.GroupsCapacityOverride] = "GroupCapacityGuard: eksplicitno prekoračenje mekog kapaciteta segmenta grupe unutar groups.manage/appointments akcija.",
        [Grants.AppointmentsMembershipBlockOverride] = "MembershipCoverageService: rezervacija člana u dugu uz postavku \"blokiraj rezervaciju\" unutar appointments/groups akcija (P2 Q54).",
        [Grants.AppointmentsAvailabilityOverride] = "AvailabilityOverride: rad izvan radnog vremena / dostupnosti uz OverrideAvailability unutar appointments/groups akcija (K2, P-2).",
        [Grants.RosterEntriesWritePast] = "RosterEntryService: upis/izmjena/brisanje roster zapisa u prošlosti unutar roster.entries.write.own/all (K2, P-4).",
        [Grants.ClientsPackagesWritePast] = "ClientPackageService.Create: ručni upis paketa s datumom kupnje prije današnjeg dana unutar clients.packages.manage (T1-9)."
    };

    private static readonly HashSet<string> CatalogKeys = Grants.Catalog.Select(g => g.Key).ToHashSet();

    private static IEnumerable<(Type Controller, MethodInfo Action, IReadOnlyList<string> Grants)> EndpointGrants()
    {
        IEnumerable<Type> controllers = typeof(RequireGrantAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (Type controller in controllers)
        foreach (MethodInfo action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            List<string> grants = new();
            foreach (MemberInfo source in new MemberInfo[] { action, controller })
            {
                if (source.GetCustomAttribute<RequireGrantAttribute>() is { } requireGrant)
                    grants.AddRange(requireGrant.Grants);
                if (source.GetCustomAttribute<RequireGrantOrAssignedCompanyAttribute>() is { } requireGrantOrCompany)
                    grants.AddRange(requireGrantOrCompany.Grants);
            }

            yield return (controller, action, grants);
        }
    }

    [Fact]
    public void GrantCatalog_HasNoDuplicateKeys()
    {
        List<string> duplicates = Grants.Catalog.GroupBy(g => g.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void GrantCatalog_EveryEntryHasDisplayNameModuleAndDescription()
    {
        Assert.All(Grants.Catalog, g =>
        {
            Assert.False(string.IsNullOrWhiteSpace(g.DisplayName), g.Key);
            Assert.False(string.IsNullOrWhiteSpace(g.Module), g.Key);
            Assert.False(string.IsNullOrWhiteSpace(g.Description), g.Key);
        });
    }

    [Fact]
    public void RequireGrant_ReferencesOnlyGrantsFromTheCatalog()
    {
        List<string> unknown = EndpointGrants()
            .SelectMany(e => e.Grants.Where(g => !CatalogKeys.Contains(g)).Select(g => $"{e.Controller.Name}.{e.Action.Name}: {g}"))
            .ToList();
        Assert.Empty(unknown);
    }

    [Fact]
    public void EveryCatalogGrant_IsUsedByAnEndpoint_OrIsAnExplicitInCodeException()
    {
        HashSet<string> used = EndpointGrants().SelectMany(e => e.Grants).ToHashSet();
        List<string> unused = CatalogKeys.Where(k => !used.Contains(k) && !CheckedInCodeOnly.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.Empty(unused);

        // Iznimka koja se ipak koristi u atributu ili više ne postoji u katalogu je zastarjela — ukloni je.
        Assert.DoesNotContain(CheckedInCodeOnly.Keys, k => used.Contains(k) || !CatalogKeys.Contains(k));
    }

    [Fact]
    public void CapabilityCatalog_HasUniqueKeys_AndReferencesOnlyCatalogGrants()
    {
        Assert.Equal(CapabilityCatalog.All.Count, CapabilityCatalog.All.Select(c => c.Key).Distinct().Count());
        Assert.All(CapabilityCatalog.All, c =>
        {
            Assert.NotEmpty(c.Grants);
            Assert.All(c.Grants, g => Assert.Contains(g.GrantKey, CatalogKeys));
        });
    }

    [Fact]
    public void CapabilityCatalog_GrantRolesMatchTheScopeModel()
    {
        Dictionary<CapabilityScopeModel, CapabilityGrantRole[]> allowed = new()
        {
            [CapabilityScopeModel.None] = new[] { CapabilityGrantRole.PrimaryNoScope, CapabilityGrantRole.MandatorySupporting },
            [CapabilityScopeModel.ViewManage] = new[] { CapabilityGrantRole.PrimaryViewOnly, CapabilityGrantRole.PrimaryManage, CapabilityGrantRole.MandatorySupporting },
            [CapabilityScopeModel.OwnAll] = new[] { CapabilityGrantRole.PrimaryOwn, CapabilityGrantRole.PrimaryAll, CapabilityGrantRole.MandatorySupporting },
            [CapabilityScopeModel.ViewOwnAll] = new[] { CapabilityGrantRole.PrimaryViewOnly, CapabilityGrantRole.PrimaryOwn, CapabilityGrantRole.PrimaryAll, CapabilityGrantRole.MandatorySupporting },
        };

        Assert.All(CapabilityCatalog.All, c => Assert.All(c.Grants, g => Assert.Contains(g.Role, allowed[c.ScopeModel])));
    }

    [Fact]
    public void CapabilityCatalog_CoversEveryCatalogGrant()
    {
        // Svaki grant je dostupan i kroz capability editor; ručni grantovi ostaju za kombinacije izvan capability modela.
        HashSet<string> covered = CapabilityCatalog.All.SelectMany(c => c.Grants).Select(g => g.GrantKey).ToHashSet();
        Assert.Empty(CatalogKeys.Where(k => !covered.Contains(k)).OrderBy(k => k));
    }
}
