using System;
using System.Security.Cryptography;
using System.Text;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Self-contained seed podaci za FAZA 3 template-version-upgrade sustav v2 (vidi
/// Migration_2026_09_27_CapabilityV2Templates.cs). Referencira POSTOJEĆE v1 CapabilityDefinition retke preko
/// CapabilityV1SeedData.CapabilityId(key) — ova faza NE mijenja/dodaje nove capability-je, samo nove
/// DefaultRoleTemplate/DefaultRoleTemplateCapability/DefaultRoleTemplateGrant retke na Version=2. Isto NAMJERNO
/// samostalno kao CapabilityV1SeedData (DatabaseMigration projekt ne ovisi o Core/Infrastructure) — vidi tamošnju
/// klasnu napomenu za obrazloženje.
///
/// Egzaktni v2 sadržaj (odobreno prije implementacije, ne izvoditi/nagađati):
/// - Admin v2: IDENTIČNO Admin v1 — svih 32 selekcije + svih 6 compatibility extras (no-op dokaz da upgrade-flow
///   ne mijenja ništa kad predložak stvarno nije promijenjen).
/// - Trener v2: svih 14 Trener v1 selekcija + NOVA "schedule.breaks.manage = Own", bez compatibility extras.
/// - Recepcija v2: svih 6 Recepcija v1 selekcija + 6 NOVIH: catalog.services.manage=View,
///   catalog.companies.manage=View, groups.manage=View, checkout.manage=Manage, products.manage=View,
///   stock.manage=View, bez compatibility extras.
/// </summary>
public static class CapabilityV2SeedData
{
    public const int TemplateVersion = 2;

    public static Guid TemplateId(string key) => DeterministicGuid($"default-role-template-v{TemplateVersion}:{key}");

    private static Guid DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }

    public static readonly CapabilityV1SeedData.TemplateSeed[] Templates =
    {
        new("admin", "Admin",
            Selections: new[]
            {
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.appointments.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.appointments.delete", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.breaks.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.entries.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.types.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.templates.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.reviews.team.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.reviews.personal.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.leave-fund.view", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.leave-fund.manage", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.leave-fund.settings.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.status.manage", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.tags.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.anonymize", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("groups.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("groups.attendance.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("checkout.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.services.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.companies.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.directory.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.role.manage", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.engagement-types.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("products.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("stock.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("commissions.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("operations.dashboard.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("operations.notifications.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("organization.branding.manage", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("organization.settings.manage", "On"),
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
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.directory.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.companies.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.services.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.tags.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.appointments.manage", "Own"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("groups.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("groups.attendance.manage", "Own"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.types.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.entries.manage", "Own"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.reviews.team.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.reviews.personal.manage", "Own"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.leave-fund.view", "Own"),
                // NOVO u v2 (odobreno prije implementacije) — Trener sad smije upravljati SVOJIM pauzama.
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.breaks.manage", "Own"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
        new("recepcija", "Recepcija",
            Selections: new[]
            {
                new CapabilityV1SeedData.TemplateCapabilitySeed("employees.directory.view", "On"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.tags.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("clients.packages.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("schedule.appointments.manage", "All"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("roster.entries.manage", "View"),
                // NOVO u v2 (odobreno prije implementacije) — 6 dodatnih selekcija za Recepciju.
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.services.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("catalog.companies.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("groups.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("checkout.manage", "Manage"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("products.manage", "View"),
                new CapabilityV1SeedData.TemplateCapabilitySeed("stock.manage", "View"),
            },
            CompatibilityExtraGrants: Array.Empty<string>()),
    };
}
