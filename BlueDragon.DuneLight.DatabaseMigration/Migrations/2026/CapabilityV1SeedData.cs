using System;
using System.Security.Cryptography;
using System.Text;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Self-contained seed podaci za FAZA 1 capability sustav v1 (vidi Migration_2026_09_26_CapabilitySystem.cs).
/// NAMJERNO ne referencira BlueDragon.DuneLight.Core (DatabaseMigration projekt je samostalan, vidi
/// SeedDefaultGrantGroupsAndBackfillOwner presedan — migracije duplicaraju podatke umjesto da ovise o kodu koji
/// se mijenja s vremenom, tako da migracija ostaje vjeran snapshot stanja u trenutku pisanja). Ako se
/// Core.Shared.Grants/DefaultGrantGroups ikad promijene, OVA datoteka se NE smije mijenjati — nova promjena ide
/// kao nova capability/template verzija u novoj migraciji.
///
/// Mapiranje je ručno izvedeno i matematički provjereno (vidi Backend Capability Phase 1 report) da:
/// - Admin v1 (svih 32 capability na max opsegu) UNION 6 CompatibilityExtra == TOČNO trenutni Grants.Catalog (63 grant-ključa).
/// - Trener v1 == TOČNO DefaultGrantGroups.TrenerGrants (21 grant-ključ).
/// - Recepcija v1 == TOČNO DefaultGrantGroups.RecepcijaGrants (9 grant-ključeva).
///
/// Dvije namjerne "grouped domain" odluke (potvrđene s korisnikom prije implementacije):
/// - catalog.services.manage nosi i catalog.packages.*/catalog.price-list.* (i na View i na Manage razini, jer
///   Trener ima packages.view/price-list.view uz services.view).
/// - catalog.companies.manage nosi i catalog.rooms.* (SAMO na Manage razini — rooms.view/rooms.manage su tagirani
///   PrimaryManage, ne PrimaryViewOnly — jer Trener ima companies.view BEZ rooms.view; da je rooms.view tagiran
///   PrimaryViewOnly, Trener-ov View odabir bi ga automatski i pogrešno dodao).
///
/// FAZA 1 HARDENING (2026-09) — products.manage/stock.manage su SEPARATE capability-ji, ne jedan grupirani
/// "products.manage" kao u prvobitnoj implementaciji. Ta ranija odluka (products+stock kao jedna capability) NIJE
/// bila eksplicitno odobrena i frontend ACTION_POLICIES već razlikuje products.manage/stock.manage kao zasebne
/// autoritete — pogrešno spojene u Fazi 1, ispravljeno prije nego što je itko koristio v1 predloške u produkciji
/// (dev-only ispravak, izmijenjena ISTA migracija/seed umjesto slojevite kompatibilnosne zakrpe, vidi
/// Migration_2026_09_26_CapabilitySystem.cs napomenu).
///
/// FAZA 1 Part L2 (2026-09) — display_name_hr/description_hr UKLONJENI iz CapabilityDefinition (i iz ovog seeda).
/// Backend capability metapodatak je jezično neutralan; frontend (Angular) lokalizira label/opis isključivo preko
/// CapabilityDefinition.Key (npr. "capabilities.schedule.appointments.manage.label"/".description"). Ne uvoditi
/// natrag hr/en tekstualna polja niti zaseban LocalizationKey — Key je već stabilan lokalizacijski identitet.
/// </summary>
public static class CapabilityV1SeedData
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
        new("schedule.appointments.manage", "schedule",
            "ViewOwnAll", "Normal", new[]
            {
                new CapabilityGrantSeed("appointments.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("appointments.write.own", "PrimaryOwn"),
                new CapabilityGrantSeed("appointments.write.all", "PrimaryAll"),
            }),
        new("schedule.appointments.delete", "schedule",
            "None", "HighRisk", new[]
            {
                new CapabilityGrantSeed("appointments.delete", "PrimaryNoScope"),
            }),
        new("schedule.breaks.manage", "schedule",
            "ViewOwnAll", "Normal", new[]
            {
                new CapabilityGrantSeed("schedule.breaks.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("schedule.breaks.write.own", "PrimaryOwn"),
                new CapabilityGrantSeed("schedule.breaks.write.all", "PrimaryAll"),
            }),
        new("roster.entries.manage", "roster",
            "ViewOwnAll", "Normal", new[]
            {
                new CapabilityGrantSeed("roster.entries.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("roster.entries.write.own", "PrimaryOwn"),
                new CapabilityGrantSeed("roster.entries.write.all", "PrimaryAll"),
            }),
        new("roster.types.manage", "roster",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("roster.types.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("roster.types.manage", "PrimaryManage"),
            }),
        new("roster.templates.manage", "roster",
            "ViewManage", "Sensitive", new[]
            {
                new CapabilityGrantSeed("roster.templates.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("roster.templates.manage", "PrimaryManage"),
            }),
        new("roster.reviews.team.view", "roster",
            "None", "Normal", new[]
            {
                new CapabilityGrantSeed("roster.reviews.team.view", "PrimaryNoScope"),
            }),
        new("roster.reviews.personal.manage", "roster",
            "OwnAll", "Sensitive", new[]
            {
                new CapabilityGrantSeed("roster.reviews.personal.view.own", "PrimaryOwn"),
                new CapabilityGrantSeed("roster.reviews.personal.view.all", "PrimaryAll"),
            }),
        new("roster.leave-fund.view", "roster",
            "OwnAll", "Normal", new[]
            {
                new CapabilityGrantSeed("roster.leave-fund.view.own", "PrimaryOwn"),
                new CapabilityGrantSeed("roster.leave-fund.view.all", "PrimaryAll"),
            }),
        new("roster.leave-fund.manage", "roster",
            "None", "Sensitive", new[]
            {
                new CapabilityGrantSeed("roster.leave-fund.manage", "PrimaryNoScope"),
            }),
        new("roster.leave-fund.settings.manage", "roster",
            "ViewManage", "Sensitive", new[]
            {
                new CapabilityGrantSeed("roster.leave-fund.settings.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("roster.leave-fund.settings.manage", "PrimaryManage"),
            }),
        new("clients.manage", "clients",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("clients.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("clients.manage", "PrimaryManage"),
            }),
        new("clients.status.manage", "clients",
            "None", "Sensitive", new[]
            {
                new CapabilityGrantSeed("clients.status.manage", "PrimaryNoScope"),
            }),
        new("clients.tags.manage", "clients",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("clients.tags.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("clients.tags.manage", "PrimaryManage"),
            }),
        new("clients.packages.manage", "clients",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("clients.packages.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("clients.packages.manage", "PrimaryManage"),
            }),
        new("clients.anonymize", "clients",
            "None", "HighRisk", new[]
            {
                new CapabilityGrantSeed("clients.anonymize", "PrimaryNoScope"),
            }),
        new("groups.manage", "groups",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("groups.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("groups.manage", "PrimaryManage"),
            }),
        new("groups.attendance.manage", "groups",
            "ViewOwnAll", "Normal", new[]
            {
                new CapabilityGrantSeed("groups.attendance.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("groups.attendance.own", "PrimaryOwn"),
                new CapabilityGrantSeed("groups.attendance.all", "PrimaryAll"),
            }),
        new("checkout.manage", "checkout",
            "ViewManage", "Sensitive", new[]
            {
                new CapabilityGrantSeed("checkout.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("checkout.manage", "PrimaryManage"),
            }),
        new("catalog.services.manage", "catalog",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("catalog.services.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("catalog.packages.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("catalog.price-list.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("catalog.services.manage", "PrimaryManage"),
                new CapabilityGrantSeed("catalog.packages.manage", "PrimaryManage"),
                new CapabilityGrantSeed("catalog.price-list.manage", "PrimaryManage"),
            }),
        new("catalog.companies.manage", "catalog",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("catalog.companies.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("catalog.companies.manage", "PrimaryManage"),
                new CapabilityGrantSeed("catalog.rooms.view", "PrimaryManage"),
                new CapabilityGrantSeed("catalog.rooms.manage", "PrimaryManage"),
            }),
        new("employees.directory.view", "employees",
            "None", "Normal", new[]
            {
                new CapabilityGrantSeed("employees.directory.view", "PrimaryNoScope"),
            }),
        new("employees.manage", "employees",
            "ViewManage", "Sensitive", new[]
            {
                new CapabilityGrantSeed("employees.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("employees.manage", "PrimaryManage"),
            }),
        new("employees.role.manage", "employees",
            "None", "HighRisk", new[]
            {
                new CapabilityGrantSeed("employees.role.manage", "PrimaryNoScope"),
            }),
        new("employees.engagement-types.manage", "employees",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("employees.engagement-types.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("employees.engagement-types.manage", "PrimaryManage"),
            }),
        new("products.manage", "products",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("products.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("products.manage", "PrimaryManage"),
            }),
        new("stock.manage", "products",
            "ViewManage", "Normal", new[]
            {
                new CapabilityGrantSeed("stock.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("stock.manage", "PrimaryManage"),
            }),
        new("commissions.manage", "commissions",
            "ViewManage", "Sensitive", new[]
            {
                new CapabilityGrantSeed("commissions.view", "PrimaryViewOnly"),
                new CapabilityGrantSeed("commissions.manage", "PrimaryManage"),
            }),
        new("operations.dashboard.view", "operations",
            "None", "Normal", new[]
            {
                new CapabilityGrantSeed("dashboard.view", "PrimaryNoScope"),
            }),
        new("operations.notifications.view", "operations",
            "None", "Normal", new[]
            {
                new CapabilityGrantSeed("notifications.view", "PrimaryNoScope"),
            }),
        new("organization.branding.manage", "organization",
            "None", "Normal", new[]
            {
                new CapabilityGrantSeed("organization.branding.manage", "PrimaryNoScope"),
            }),
        new("organization.settings.manage", "organization",
            "None", "Sensitive", new[]
            {
                new CapabilityGrantSeed("organization.settings.manage", "PrimaryNoScope"),
            }),
    };

    public static readonly TemplateSeed[] Templates =
    {
        new("admin", "Admin",
            Selections: new[]
            {
                new TemplateCapabilitySeed("schedule.appointments.manage", "All"),
                new TemplateCapabilitySeed("schedule.appointments.delete", "On"),
                new TemplateCapabilitySeed("schedule.breaks.manage", "All"),
                new TemplateCapabilitySeed("roster.entries.manage", "All"),
                new TemplateCapabilitySeed("roster.types.manage", "Manage"),
                new TemplateCapabilitySeed("roster.templates.manage", "Manage"),
                new TemplateCapabilitySeed("roster.reviews.team.view", "On"),
                new TemplateCapabilitySeed("roster.reviews.personal.manage", "All"),
                new TemplateCapabilitySeed("roster.leave-fund.view", "All"),
                new TemplateCapabilitySeed("roster.leave-fund.manage", "On"),
                new TemplateCapabilitySeed("roster.leave-fund.settings.manage", "Manage"),
                new TemplateCapabilitySeed("clients.manage", "Manage"),
                new TemplateCapabilitySeed("clients.status.manage", "On"),
                new TemplateCapabilitySeed("clients.tags.manage", "Manage"),
                new TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new TemplateCapabilitySeed("clients.anonymize", "On"),
                new TemplateCapabilitySeed("groups.manage", "Manage"),
                new TemplateCapabilitySeed("groups.attendance.manage", "All"),
                new TemplateCapabilitySeed("checkout.manage", "Manage"),
                new TemplateCapabilitySeed("catalog.services.manage", "Manage"),
                new TemplateCapabilitySeed("catalog.companies.manage", "Manage"),
                new TemplateCapabilitySeed("employees.directory.view", "On"),
                new TemplateCapabilitySeed("employees.manage", "Manage"),
                new TemplateCapabilitySeed("employees.role.manage", "On"),
                new TemplateCapabilitySeed("employees.engagement-types.manage", "Manage"),
                new TemplateCapabilitySeed("products.manage", "Manage"),
                new TemplateCapabilitySeed("stock.manage", "Manage"),
                new TemplateCapabilitySeed("commissions.manage", "Manage"),
                new TemplateCapabilitySeed("operations.dashboard.view", "On"),
                new TemplateCapabilitySeed("operations.notifications.view", "On"),
                new TemplateCapabilitySeed("organization.branding.manage", "On"),
                new TemplateCapabilitySeed("organization.settings.manage", "On"),
            },
            CompatibilityExtraGrants: new[]
            {
                "appointments.write.own",
                "schedule.breaks.write.own",
                "roster.entries.write.own",
                "groups.attendance.own",
                "roster.reviews.personal.view.own",
                "roster.leave-fund.view.own",
            }),
        new("trener", "Trener",
            Selections: new[]
            {
                new TemplateCapabilitySeed("employees.directory.view", "On"),
                new TemplateCapabilitySeed("catalog.companies.manage", "View"),
                new TemplateCapabilitySeed("catalog.services.manage", "View"),
                new TemplateCapabilitySeed("clients.manage", "Manage"),
                new TemplateCapabilitySeed("clients.tags.manage", "View"),
                new TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new TemplateCapabilitySeed("schedule.appointments.manage", "Own"),
                new TemplateCapabilitySeed("groups.manage", "View"),
                new TemplateCapabilitySeed("groups.attendance.manage", "Own"),
                new TemplateCapabilitySeed("roster.types.manage", "View"),
                new TemplateCapabilitySeed("roster.entries.manage", "Own"),
                new TemplateCapabilitySeed("roster.reviews.team.view", "On"),
                new TemplateCapabilitySeed("roster.reviews.personal.manage", "Own"),
                new TemplateCapabilitySeed("roster.leave-fund.view", "Own"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
        new("recepcija", "Recepcija",
            Selections: new[]
            {
                new TemplateCapabilitySeed("employees.directory.view", "On"),
                new TemplateCapabilitySeed("clients.manage", "Manage"),
                new TemplateCapabilitySeed("clients.tags.manage", "View"),
                new TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new TemplateCapabilitySeed("schedule.appointments.manage", "All"),
                new TemplateCapabilitySeed("roster.entries.manage", "View"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
    };
}
