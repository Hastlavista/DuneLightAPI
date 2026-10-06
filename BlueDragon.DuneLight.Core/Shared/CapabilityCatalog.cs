using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Statični katalog capabilityja (ADR-0023) — capability je samo editorska projekcija nad raw grantovima iz
/// <see cref="Grants.Catalog"/>: grupira grantove jednog područja u scope model (View/Manage, Own/All...) za role
/// editor. Nema verzija, deprecated stanja ni zapisa u bazi; jedini perzistirani autorizacijski izvor istine je
/// GrantGroupGrant, a runtime autorizacija čita isključivo raw grantove.
/// </summary>
public static class CapabilityCatalog
{
    public static readonly IReadOnlyList<CapabilityDefinition> All = new List<CapabilityDefinition>
    {
        ViewManage("catalog.companies.manage", "catalog", CapabilitySensitivity.Normal,
            view: new[] { Grants.CatalogCompaniesView },
            manage: new[] { Grants.CatalogCompaniesManage, Grants.CatalogRoomsView, Grants.CatalogRoomsManage }),
        ViewManage("catalog.resources.manage", "catalog", CapabilitySensitivity.Normal,
            view: new[] { Grants.CatalogResourcesView },
            manage: new[] { Grants.CatalogResourcesManage }),
        ViewManage("catalog.services.manage", "catalog", CapabilitySensitivity.Normal,
            view: new[] { Grants.CatalogServicesView, Grants.CatalogPackagesView, Grants.CatalogPriceListView },
            manage: new[] { Grants.CatalogServicesManage, Grants.CatalogPackagesManage, Grants.CatalogPriceListManage }),

        ViewManage("checkout.manage", "checkout", CapabilitySensitivity.Sensitive,
            view: new[] { Grants.CheckoutView },
            manage: new[] { Grants.CheckoutManage }),

        On("clients.anonymize", "clients", CapabilitySensitivity.HighRisk, Grants.ClientsAnonymize),
        ViewManage("clients.manage", "clients", CapabilitySensitivity.Normal,
            view: new[] { Grants.ClientsView },
            manage: new[] { Grants.ClientsManage }),
        ViewManage("clients.packages.manage", "clients", CapabilitySensitivity.Normal,
            view: new[] { Grants.ClientsPackagesView },
            manage: new[] { Grants.ClientsPackagesManage }),
        On("clients.status.manage", "clients", CapabilitySensitivity.Sensitive, Grants.ClientsStatusManage),
        ViewManage("clients.tags.manage", "clients", CapabilitySensitivity.Normal,
            view: new[] { Grants.ClientsTagsView },
            manage: new[] { Grants.ClientsTagsManage }),

        ViewManage("commissions.manage", "commissions", CapabilitySensitivity.Sensitive,
            view: new[] { Grants.CommissionsView },
            manage: new[] { Grants.CommissionsManage }),

        On("employees.directory.view", "employees", CapabilitySensitivity.Normal, Grants.EmployeesDirectoryView),
        ViewManage("employees.engagement-types.manage", "employees", CapabilitySensitivity.Normal,
            view: new[] { Grants.EmployeesEngagementTypesView },
            manage: new[] { Grants.EmployeesEngagementTypesManage }),
        ViewManage("employees.manage", "employees", CapabilitySensitivity.Sensitive,
            view: new[] { Grants.EmployeesView },
            manage: new[] { Grants.EmployeesManage }),

        ViewOwnAll("groups.attendance.manage", "groups", CapabilitySensitivity.Normal,
            view: Grants.GroupsAttendanceView, own: Grants.GroupsAttendanceOwn, all: Grants.GroupsAttendanceAll),
        On("groups.capacity.override", "groups", CapabilitySensitivity.Sensitive, Grants.GroupsCapacityOverride),
        ViewManage("groups.manage", "groups", CapabilitySensitivity.Normal,
            view: new[] { Grants.GroupsView },
            manage: new[] { Grants.GroupsManage }),

        On("operations.dashboard.view", "operations", CapabilitySensitivity.Normal, Grants.DashboardView),
        On("operations.notifications.view", "operations", CapabilitySensitivity.Normal, Grants.NotificationsView),

        On("organization.branding.manage", "organization", CapabilitySensitivity.Normal, Grants.OrganizationBrandingManage),
        ViewManage("organization.permissions.manage", "organization", CapabilitySensitivity.HighRisk,
            view: new[] { Grants.PermissionsView },
            manage: new[] { Grants.PermissionsManage, Grants.PermissionsAssignmentsManage }),
        On("organization.settings.manage", "organization", CapabilitySensitivity.Sensitive, Grants.OrganizationSettingsManage),

        ViewManage("products.manage", "products", CapabilitySensitivity.Normal,
            view: new[] { Grants.ProductsView },
            manage: new[] { Grants.ProductsManage }),
        ViewManage("stock.manage", "products", CapabilitySensitivity.Normal,
            view: new[] { Grants.StockView },
            manage: new[] { Grants.StockManage }),

        ViewOwnAll("roster.entries.manage", "roster", CapabilitySensitivity.Normal,
            view: Grants.RosterEntriesView, own: Grants.RosterEntriesWriteOwn, all: Grants.RosterEntriesWriteAll),
        On("roster.leave-fund.manage", "roster", CapabilitySensitivity.Sensitive, Grants.RosterLeaveFundManage),
        ViewManage("roster.leave-fund.settings.manage", "roster", CapabilitySensitivity.Sensitive,
            view: new[] { Grants.RosterLeaveFundSettingsView },
            manage: new[] { Grants.RosterLeaveFundSettingsManage }),
        OwnAll("roster.leave-fund.view", "roster", CapabilitySensitivity.Normal,
            own: Grants.RosterLeaveFundViewOwn, all: Grants.RosterLeaveFundViewAll),
        OwnAll("roster.reviews.personal.manage", "roster", CapabilitySensitivity.Sensitive,
            own: Grants.RosterReviewsPersonalViewOwn, all: Grants.RosterReviewsPersonalViewAll),
        On("roster.reviews.team.view", "roster", CapabilitySensitivity.Normal, Grants.RosterReviewsTeamView),
        ViewManage("roster.templates.manage", "roster", CapabilitySensitivity.Sensitive,
            view: new[] { Grants.RosterTemplatesView },
            manage: new[] { Grants.RosterTemplatesManage }),
        ViewManage("roster.types.manage", "roster", CapabilitySensitivity.Normal,
            view: new[] { Grants.RosterTypesView },
            manage: new[] { Grants.RosterTypesManage }),

        On("schedule.appointments.delete", "schedule", CapabilitySensitivity.HighRisk, Grants.AppointmentsDelete),
        ViewOwnAll("schedule.appointments.manage", "schedule", CapabilitySensitivity.Normal,
            view: Grants.AppointmentsView, own: Grants.AppointmentsWriteOwn, all: Grants.AppointmentsWriteAll),
        ViewOwnAll("schedule.breaks.manage", "schedule", CapabilitySensitivity.Normal,
            view: Grants.ScheduleBreaksView, own: Grants.ScheduleBreaksWriteOwn, all: Grants.ScheduleBreaksWriteAll),
    };

    private static readonly Dictionary<string, CapabilityDefinition> ByKey = All.ToDictionary(c => c.Key);

    /// <summary>NULL ako capability s tim ključem ne postoji.</summary>
    public static CapabilityDefinition Find(string key) => key != null && ByKey.TryGetValue(key, out CapabilityDefinition definition) ? definition : null;

    private static CapabilityDefinition On(string key, string category, CapabilitySensitivity sensitivity, string grant) =>
        new(key, category, CapabilityScopeModel.None, sensitivity,
            new[] { new CapabilityGrantRoleEntry(grant, CapabilityGrantRole.PrimaryNoScope) });

    private static CapabilityDefinition ViewManage(string key, string category, CapabilitySensitivity sensitivity, string[] view, string[] manage) =>
        new(key, category, CapabilityScopeModel.ViewManage, sensitivity,
            view.Select(g => new CapabilityGrantRoleEntry(g, CapabilityGrantRole.PrimaryViewOnly))
                .Concat(manage.Select(g => new CapabilityGrantRoleEntry(g, CapabilityGrantRole.PrimaryManage)))
                .ToList());

    private static CapabilityDefinition OwnAll(string key, string category, CapabilitySensitivity sensitivity, string own, string all) =>
        new(key, category, CapabilityScopeModel.OwnAll, sensitivity,
            new[]
            {
                new CapabilityGrantRoleEntry(own, CapabilityGrantRole.PrimaryOwn),
                new CapabilityGrantRoleEntry(all, CapabilityGrantRole.PrimaryAll)
            });

    private static CapabilityDefinition ViewOwnAll(string key, string category, CapabilitySensitivity sensitivity, string view, string own, string all) =>
        new(key, category, CapabilityScopeModel.ViewOwnAll, sensitivity,
            new[]
            {
                new CapabilityGrantRoleEntry(view, CapabilityGrantRole.PrimaryViewOnly),
                new CapabilityGrantRoleEntry(own, CapabilityGrantRole.PrimaryOwn),
                new CapabilityGrantRoleEntry(all, CapabilityGrantRole.PrimaryAll)
            });
}

/// <summary>Jedan capability iz <see cref="CapabilityCatalog"/>: ključ, kategorija (područje), scope model,
/// osjetljivost (autorsko upozorenje u UI-ju, ne utječe na autorizaciju) i grantovi s ulogama koje određuju pri kojem
/// odabranom opsegu se materijaliziraju (vidi ICapabilityMaterializationService).</summary>
public record CapabilityDefinition(
    string Key,
    string CategoryKey,
    CapabilityScopeModel ScopeModel,
    CapabilitySensitivity Sensitivity,
    IReadOnlyList<CapabilityGrantRoleEntry> Grants);
