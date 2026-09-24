using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Grant-only Tenant Authorization Refactor, Part N/O — prije ove faze GrantGroups/Grants/Capabilities/Roles/
/// GrantDiagnostics kontroleri su bili potpuno Owner-only ([RequireOwner]), pa permission-administracija NIKAD
/// nije bila predstavljena u capability sustavu. Dodaje TOČNO JEDNU novu capability ("organization.permissions.manage",
/// ViewManage, HighRisk) koja pokriva sva tri nova raw granta (permissions.view/manage/assignments.manage — vidi
/// Grants.cs), i NOVU "admin" DefaultRoleTemplate verziju (v3, identičnu v2 + ovu jednu dodatnu capability na
/// Manage razini). Trener/Recepcija NE dobivaju v3 (namjerno — permission-administracija ostaje Admin-only
/// starter ovlast; tenant koji to želi drugoj GrantGroup-i može to ručno dodati preko role-editora).
/// Ne mijenja CapabilityV1SeedData/CapabilityV2SeedData (immutable snapshotovi) — ovaj seed je samostalan, po
/// istom obrascu (migracija duplicira podatke umjesto da ovisi o kodu koji se mijenja s vremenom).
/// </summary>
public static class PermissionAdministrationCapabilitySeedData
{
    public const int CapabilityVersion = 1;
    public const int AdminTemplateVersion = 3;

    public const string CapabilityKey = "organization.permissions.manage";

    public static Guid CapabilityId() => DeterministicGuid($"capability-definition-v{CapabilityVersion}:{CapabilityKey}");

    public static Guid AdminTemplateId() => DeterministicGuid($"default-role-template-v{AdminTemplateVersion}:admin");

    private static Guid DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }

    public static readonly (string GrantKey, string Role)[] Grants =
    {
        ("permissions.view", "PrimaryViewOnly"),
        ("permissions.manage", "PrimaryManage"),
        ("permissions.assignments.manage", "PrimaryManage"),
    };
}

[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 0)]
public class SeedOrganizationPermissionsCapability : DuneLightMigration
{
    public override void Up()
    {
        Guid capabilityId = PermissionAdministrationCapabilitySeedData.CapabilityId();

        Insert.IntoTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = capabilityId,
            key = PermissionAdministrationCapabilitySeedData.CapabilityKey,
            version = PermissionAdministrationCapabilitySeedData.CapabilityVersion,
            category_key = "organization",
            // display_name_hr/description_hr ne postoje u shemi (CreateCapabilityDefinitionsTable ih ne stvara);
            // frontend lokalizira preko Key-a.
            scope_model = "ViewManage",
            sensitivity = "HighRisk",
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        });

        foreach ((string grantKey, string role) in PermissionAdministrationCapabilitySeedData.Grants)
        {
            Insert.IntoTable(Tables.CapabilityDefinitionGrants).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                capability_definition_id = capabilityId,
                grant_key = grantKey,
                role
            });
        }
    }
}

/// <summary>Admin v3 = Admin v2 (svih 32 selekcije, IDENTIČNO CapabilityV2SeedData) + JEDNA nova selekcija
/// (organization.permissions.manage = Manage) + isti 6 compatibility extras. Trener/Recepcija ostaju na v2 —
/// ne objavljuje se nova verzija za njih u ovoj migraciji.</summary>
[DeveloperMigration(2026, 09, 29, Developer.SilvioHabazin, 1)]
public class SeedAdminTemplateV3 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid templateId = PermissionAdministrationCapabilitySeedData.AdminTemplateId();

        CapabilityV1SeedData.TemplateSeed adminV2 = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin");

        Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = templateId,
            key = "admin",
            version = PermissionAdministrationCapabilitySeedData.AdminTemplateVersion,
            display_name_hr = adminV2.DisplayNameHr,
            is_active = true,
            created_at = now
        });

        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in adminV2.Selections)
        {
            Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                default_role_template_id = templateId,
                capability_definition_id = CapabilityV1SeedData.CapabilityId(selection.CapabilityKey),
                selected_scope = selection.SelectedScope
            });
        }

        // Nova capability u v3 — jedina razlika prema v2.
        Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            default_role_template_id = templateId,
            capability_definition_id = PermissionAdministrationCapabilitySeedData.CapabilityId(),
            selected_scope = "Manage"
        });

        foreach (string grantKey in adminV2.CompatibilityExtraGrants)
        {
            Insert.IntoTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                default_role_template_id = templateId,
                grant_key = grantKey,
                reason = "CompatibilityExtra"
            });
        }
    }
}
