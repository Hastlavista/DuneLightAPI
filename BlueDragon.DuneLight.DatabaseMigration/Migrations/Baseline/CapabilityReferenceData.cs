using System;
using System.Security.Cryptography;
using System.Text;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations.Baseline;

/// <summary>
/// JEDINSTVENA autoritativna reprezentacija referentnih podataka za capability sustav (baseline). Zamjenjuje bivše
/// CapabilityV1SeedData/CapabilityV2SeedData (+ V3 Admin) generacije: ovdje je samo KONAČNO stanje, verzija 1.
/// Samostalno (DatabaseMigration projekt ne ovisi o Core/Infrastructure) — Baseline002PermissionsAndCapabilities
/// ovo upisuje, a UnitTests ovo koriste kao izvor stvarnih capability/template podataka. Ako se Grants/capability
/// katalog promijeni, mijenja se OVA datoteka (baseline se prepisuje dok nema produkcije), ne slažu se nove
/// generacije seed migracija.
/// </summary>
public static class CapabilityReferenceData
{
    public const int CapabilityVersion = 1;
    public const int TemplateVersion = 1;

    public record CapabilityGrantSeed(string GrantKey, string Role);

    public record CapabilitySeed(string Key, string CategoryKey, string ScopeModel, string Sensitivity, CapabilityGrantSeed[] Grants);

    public record TemplateCapabilitySeed(string CapabilityKey, string SelectedScope);

    public record TemplateSeed(string Key, string DisplayNameHr, TemplateCapabilitySeed[] Selections, string[] CompatibilityExtraGrants);

    public static Guid CapabilityId(string key) => DeterministicGuid($"capability-definition-v{CapabilityVersion}:{key}");

    public static Guid TemplateId(string key) => DeterministicGuid($"default-role-template-v{TemplateVersion}:{key}");

    private static Guid DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }

    public static readonly CapabilitySeed[] Capabilities =
    {
        new("catalog.companies.manage", "catalog", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("catalog.companies.manage", "PrimaryManage"),
            new("catalog.rooms.manage", "PrimaryManage"),
            new("catalog.rooms.view", "PrimaryManage"),
            new("catalog.companies.view", "PrimaryViewOnly"),
        }),
        new("catalog.services.manage", "catalog", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("catalog.packages.manage", "PrimaryManage"),
            new("catalog.price-list.manage", "PrimaryManage"),
            new("catalog.services.manage", "PrimaryManage"),
            new("catalog.packages.view", "PrimaryViewOnly"),
            new("catalog.price-list.view", "PrimaryViewOnly"),
            new("catalog.services.view", "PrimaryViewOnly"),
        }),
        new("checkout.manage", "checkout", "ViewManage", "Sensitive", new CapabilityGrantSeed[]
        {
            new("checkout.manage", "PrimaryManage"),
            new("checkout.view", "PrimaryViewOnly"),
        }),
        new("clients.anonymize", "clients", "None", "HighRisk", new CapabilityGrantSeed[]
        {
            new("clients.anonymize", "PrimaryNoScope"),
        }),
        new("clients.manage", "clients", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("clients.manage", "PrimaryManage"),
            new("clients.view", "PrimaryViewOnly"),
        }),
        new("clients.packages.manage", "clients", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("clients.packages.manage", "PrimaryManage"),
            new("clients.packages.view", "PrimaryViewOnly"),
        }),
        new("clients.status.manage", "clients", "None", "Sensitive", new CapabilityGrantSeed[]
        {
            new("clients.status.manage", "PrimaryNoScope"),
        }),
        new("clients.tags.manage", "clients", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("clients.tags.manage", "PrimaryManage"),
            new("clients.tags.view", "PrimaryViewOnly"),
        }),
        new("commissions.manage", "commissions", "ViewManage", "Sensitive", new CapabilityGrantSeed[]
        {
            new("commissions.manage", "PrimaryManage"),
            new("commissions.view", "PrimaryViewOnly"),
        }),
        new("employees.directory.view", "employees", "None", "Normal", new CapabilityGrantSeed[]
        {
            new("employees.directory.view", "PrimaryNoScope"),
        }),
        new("employees.engagement-types.manage", "employees", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("employees.engagement-types.manage", "PrimaryManage"),
            new("employees.engagement-types.view", "PrimaryViewOnly"),
        }),
        new("employees.manage", "employees", "ViewManage", "Sensitive", new CapabilityGrantSeed[]
        {
            new("employees.manage", "PrimaryManage"),
            new("employees.view", "PrimaryViewOnly"),
        }),
        new("employees.role.manage", "employees", "None", "HighRisk", new CapabilityGrantSeed[]
        {
            new("employees.role.manage", "PrimaryNoScope"),
        }),
        new("groups.attendance.manage", "groups", "ViewOwnAll", "Normal", new CapabilityGrantSeed[]
        {
            new("groups.attendance.all", "PrimaryAll"),
            new("groups.attendance.own", "PrimaryOwn"),
            new("groups.attendance.view", "PrimaryViewOnly"),
        }),
        new("groups.manage", "groups", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("groups.manage", "PrimaryManage"),
            new("groups.view", "PrimaryViewOnly"),
        }),
        new("operations.dashboard.view", "operations", "None", "Normal", new CapabilityGrantSeed[]
        {
            new("dashboard.view", "PrimaryNoScope"),
        }),
        new("operations.notifications.view", "operations", "None", "Normal", new CapabilityGrantSeed[]
        {
            new("notifications.view", "PrimaryNoScope"),
        }),
        new("organization.branding.manage", "organization", "None", "Normal", new CapabilityGrantSeed[]
        {
            new("organization.branding.manage", "PrimaryNoScope"),
        }),
        new("organization.permissions.manage", "organization", "ViewManage", "HighRisk", new CapabilityGrantSeed[]
        {
            new("permissions.assignments.manage", "PrimaryManage"),
            new("permissions.manage", "PrimaryManage"),
            new("permissions.view", "PrimaryViewOnly"),
        }),
        new("organization.settings.manage", "organization", "None", "Sensitive", new CapabilityGrantSeed[]
        {
            new("organization.settings.manage", "PrimaryNoScope"),
        }),
        new("products.manage", "products", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("products.manage", "PrimaryManage"),
            new("products.view", "PrimaryViewOnly"),
        }),
        new("stock.manage", "products", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("stock.manage", "PrimaryManage"),
            new("stock.view", "PrimaryViewOnly"),
        }),
        new("roster.entries.manage", "roster", "ViewOwnAll", "Normal", new CapabilityGrantSeed[]
        {
            new("roster.entries.write.all", "PrimaryAll"),
            new("roster.entries.write.own", "PrimaryOwn"),
            new("roster.entries.view", "PrimaryViewOnly"),
        }),
        new("roster.leave-fund.manage", "roster", "None", "Sensitive", new CapabilityGrantSeed[]
        {
            new("roster.leave-fund.manage", "PrimaryNoScope"),
        }),
        new("roster.leave-fund.settings.manage", "roster", "ViewManage", "Sensitive", new CapabilityGrantSeed[]
        {
            new("roster.leave-fund.settings.manage", "PrimaryManage"),
            new("roster.leave-fund.settings.view", "PrimaryViewOnly"),
        }),
        new("roster.leave-fund.view", "roster", "OwnAll", "Normal", new CapabilityGrantSeed[]
        {
            new("roster.leave-fund.view.all", "PrimaryAll"),
            new("roster.leave-fund.view.own", "PrimaryOwn"),
        }),
        new("roster.reviews.personal.manage", "roster", "OwnAll", "Sensitive", new CapabilityGrantSeed[]
        {
            new("roster.reviews.personal.view.all", "PrimaryAll"),
            new("roster.reviews.personal.view.own", "PrimaryOwn"),
        }),
        new("roster.reviews.team.view", "roster", "None", "Normal", new CapabilityGrantSeed[]
        {
            new("roster.reviews.team.view", "PrimaryNoScope"),
        }),
        new("roster.templates.manage", "roster", "ViewManage", "Sensitive", new CapabilityGrantSeed[]
        {
            new("roster.templates.manage", "PrimaryManage"),
            new("roster.templates.view", "PrimaryViewOnly"),
        }),
        new("roster.types.manage", "roster", "ViewManage", "Normal", new CapabilityGrantSeed[]
        {
            new("roster.types.manage", "PrimaryManage"),
            new("roster.types.view", "PrimaryViewOnly"),
        }),
        new("schedule.appointments.delete", "schedule", "None", "HighRisk", new CapabilityGrantSeed[]
        {
            new("appointments.delete", "PrimaryNoScope"),
        }),
        new("schedule.appointments.manage", "schedule", "ViewOwnAll", "Normal", new CapabilityGrantSeed[]
        {
            new("appointments.write.all", "PrimaryAll"),
            new("appointments.write.own", "PrimaryOwn"),
            new("appointments.view", "PrimaryViewOnly"),
        }),
        new("schedule.breaks.manage", "schedule", "ViewOwnAll", "Normal", new CapabilityGrantSeed[]
        {
            new("schedule.breaks.write.all", "PrimaryAll"),
            new("schedule.breaks.write.own", "PrimaryOwn"),
            new("schedule.breaks.view", "PrimaryViewOnly"),
        }),
    };

    public static readonly TemplateSeed[] Templates =
    {
        new("admin", "Admin",
            Selections: new TemplateCapabilitySeed[]
            {
                new("catalog.companies.manage", "Manage"),
                new("catalog.services.manage", "Manage"),
                new("checkout.manage", "Manage"),
                new("clients.anonymize", "On"),
                new("clients.manage", "Manage"),
                new("clients.packages.manage", "Manage"),
                new("clients.status.manage", "On"),
                new("clients.tags.manage", "Manage"),
                new("commissions.manage", "Manage"),
                new("employees.directory.view", "On"),
                new("employees.engagement-types.manage", "Manage"),
                new("employees.manage", "Manage"),
                new("employees.role.manage", "On"),
                new("groups.attendance.manage", "All"),
                new("groups.manage", "Manage"),
                new("operations.dashboard.view", "On"),
                new("operations.notifications.view", "On"),
                new("organization.branding.manage", "On"),
                new("organization.permissions.manage", "Manage"),
                new("organization.settings.manage", "On"),
                new("products.manage", "Manage"),
                new("roster.entries.manage", "All"),
                new("roster.leave-fund.manage", "On"),
                new("roster.leave-fund.settings.manage", "Manage"),
                new("roster.leave-fund.view", "All"),
                new("roster.reviews.personal.manage", "All"),
                new("roster.reviews.team.view", "On"),
                new("roster.templates.manage", "Manage"),
                new("roster.types.manage", "Manage"),
                new("schedule.appointments.delete", "On"),
                new("schedule.appointments.manage", "All"),
                new("schedule.breaks.manage", "All"),
                new("stock.manage", "Manage"),
            },
            CompatibilityExtraGrants: new[]
            {
                "appointments.write.own",
                "groups.attendance.own",
                "roster.entries.write.own",
                "roster.leave-fund.view.own",
                "roster.reviews.personal.view.own",
                "schedule.breaks.write.own",
            }),
        new("recepcija", "Recepcija",
            Selections: new TemplateCapabilitySeed[]
            {
                new("catalog.companies.manage", "View"),
                new("catalog.services.manage", "View"),
                new("checkout.manage", "Manage"),
                new("clients.manage", "Manage"),
                new("clients.packages.manage", "Manage"),
                new("clients.tags.manage", "View"),
                new("employees.directory.view", "On"),
                new("groups.manage", "View"),
                new("products.manage", "View"),
                new("roster.entries.manage", "View"),
                new("schedule.appointments.manage", "All"),
                new("stock.manage", "View"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
        new("trener", "Trener",
            Selections: new TemplateCapabilitySeed[]
            {
                new("catalog.companies.manage", "View"),
                new("catalog.services.manage", "View"),
                new("clients.manage", "Manage"),
                new("clients.packages.manage", "Manage"),
                new("clients.tags.manage", "View"),
                new("employees.directory.view", "On"),
                new("groups.attendance.manage", "Own"),
                new("groups.manage", "View"),
                new("roster.entries.manage", "Own"),
                new("roster.leave-fund.view", "Own"),
                new("roster.reviews.personal.manage", "Own"),
                new("roster.reviews.team.view", "On"),
                new("roster.types.manage", "View"),
                new("schedule.appointments.manage", "Own"),
                new("schedule.breaks.manage", "Own"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
    };
}
