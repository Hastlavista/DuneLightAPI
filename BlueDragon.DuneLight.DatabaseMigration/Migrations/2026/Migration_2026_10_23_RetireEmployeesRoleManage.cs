using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// ADR-0019 (foundation cleanup) — povlačenje granta/capabilityja "employees.role.manage". Štitio je isključivo
/// PATCH /api/employees/{id}/role (legacy UserRole), koji je uklonjen, pa više ne štiti nijednu poslovnu mogućnost.
/// Po postojećem mehanizmu verzioniranja: NOVA "admin" DefaultRoleTemplate verzija (v6 = v5 bez employees.role.manage)
/// se objavljuje, a capability verzija se povlači (is_active = false + deprecated_at) — referencirane capability verzije
/// se nikad ne brišu fizički (FK Restrict, vidi CapabilityVersionGuard). Trener/Recepcija predlošci ga nikad nisu imali.
/// Samostalni seed (bez ovisnosti o Core), isto kao ostali capability seedovi.
/// </summary>
public static class EmployeesRoleManageRetirementSeedData
{
    public const int AdminTemplateVersion = 6;

    public const string RetiredCapabilityKey = "employees.role.manage";
    public const string RetiredGrantKey = "employees.role.manage";

    public static Guid RetiredCapabilityId() => CapabilityV1SeedData.CapabilityId(RetiredCapabilityKey);

    public static Guid AdminTemplateId() => DeterministicGuid($"default-role-template-v{AdminTemplateVersion}:admin");

    private static Guid DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(seed)));
    }
}

/// <summary>Admin v6 = Admin v5 (vidi SeedAdminTemplateV5) BEZ employees.role.manage + isti compatibility extras.</summary>
[DeveloperMigration(2026, 10, 23, Developer.SilvioHabazin, 1)]
public class SeedAdminTemplateV6 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid templateId = EmployeesRoleManageRetirementSeedData.AdminTemplateId();
        CapabilityV1SeedData.TemplateSeed adminV2 = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin");

        Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = templateId,
            key = "admin",
            version = EmployeesRoleManageRetirementSeedData.AdminTemplateVersion,
            display_name_hr = adminV2.DisplayNameHr,
            is_active = true,
            created_at = now
        });

        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in adminV2.Selections
                     .Where(s => s.CapabilityKey != EmployeesRoleManageRetirementSeedData.RetiredCapabilityKey))
            InsertSelection(templateId, CapabilityV1SeedData.CapabilityId(selection.CapabilityKey), selection.SelectedScope);

        InsertSelection(templateId, PermissionAdministrationCapabilitySeedData.CapabilityId(), "Manage");
        InsertSelection(templateId, ResourcesCapabilitySeedData.CapabilityId(), "Manage");
        InsertSelection(templateId, GroupCapacityOverrideCapabilitySeedData.CapabilityId(), "On");

        foreach (string grantKey in adminV2.CompatibilityExtraGrants)
            Insert.IntoTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                default_role_template_id = templateId,
                grant_key = grantKey,
                reason = "CompatibilityExtra"
            });
    }

    private void InsertSelection(Guid templateId, Guid capabilityDefinitionId, string selectedScope)
    {
        Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            default_role_template_id = templateId,
            capability_definition_id = capabilityDefinitionId,
            selected_scope = selectedScope
        });
    }
}

/// <summary>
/// Povlači capability employees.role.manage v1 i čisti postojeće reference:
/// 1. capability verzija: is_active = false, deprecated_at = now (ostaje kao nepromjenjiva povijest);
/// 2. svi "admin" predlošci koji je biraju (v1–v5) se deaktiviraju — zamijenjeni su s v6, a aktivan predložak ne smije
///    referencirati deprecated capability (vidi GrantDiagnosticsService TemplateReferencesInvalidCapabilityVersion);
///    eksplicitno učitavanje po verziji (upgrade BASE) i dalje radi jer ne filtrira po is_active;
/// 3. tenant GrantGroup-e: uklanja se sirovi grant, capability snapshot i template provenance za taj ključ. Nakon toga
///    upgrade v5 -> v6 za takvu grupu nema konflikta (Current == Target == None, vidi TemplateUpgradePlanner slučaj 3).
/// </summary>
[DeveloperMigration(2026, 10, 23, Developer.SilvioHabazin, 2)]
public class RetireEmployeesRoleManageCapability : DuneLightMigration
{
    public override void Up()
    {
        Guid capabilityId = EmployeesRoleManageRetirementSeedData.RetiredCapabilityId();
        string grantKey = EmployeesRoleManageRetirementSeedData.RetiredGrantKey;

        Execute.Sql($@"
            UPDATE dunelight.{Tables.CapabilityDefinitions}
               SET is_active = false, deprecated_at = now()
             WHERE id = '{capabilityId}';

            UPDATE dunelight.{Tables.DefaultRoleTemplates} t
               SET is_active = false
             WHERE t.is_active
               AND EXISTS (SELECT 1 FROM dunelight.{Tables.DefaultRoleTemplateCapabilities} c
                            WHERE c.default_role_template_id = t.id AND c.capability_definition_id = '{capabilityId}');

            DELETE FROM dunelight.{Tables.GrantGroupCapabilitySnapshots} WHERE capability_definition_id = '{capabilityId}';
            DELETE FROM dunelight.{Tables.GrantGroupTemplateGrants} WHERE grant_key = '{grantKey}';
            DELETE FROM dunelight.{Tables.GrantGroupGrants} WHERE grant_key = '{grantKey}';");
    }
}
