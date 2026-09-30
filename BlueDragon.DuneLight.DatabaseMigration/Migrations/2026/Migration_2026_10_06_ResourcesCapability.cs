using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Resource katalog — autorizacija kroz postojeći capability sustav, po istom obrascu kao
/// PermissionAdministrationCapabilitySeedData (Migration_2026_09_29): JEDNA nova capability
/// ("catalog.resources.manage", ViewManage, Normal) koja pokriva raw grantove catalog.resources.view (PrimaryViewOnly)
/// i catalog.resources.manage (PrimaryManage), te NOVA "admin" DefaultRoleTemplate verzija (v4 = v3 + ova capability na
/// Manage razini). Postojeće capability definicije (catalog.companies.manage s rooms.*) i v1/v2/v3 predlošci su
/// immutable snapshotovi i ne mijenjaju se. Trener/Recepcija ne dobivaju novu verziju (kao ni rooms.* u izvornom opsegu).
/// Objava predloška je čisto metapodatkovna — postojeće tenant GrantGroup-e se ne diraju; do resursa dolaze kroz
/// postojeći template-upgrade tok (v3 -&gt; v4), a nove organizacije automatski (najnoviji aktivni "admin" predložak).
/// Samostalni seed (bez ovisnosti o Core), isto kao ostali capability seedovi.
/// </summary>
public static class ResourcesCapabilitySeedData
{
    public const int CapabilityVersion = 1;
    public const int AdminTemplateVersion = 4;

    public const string CapabilityKey = "catalog.resources.manage";

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
        ("catalog.resources.view", "PrimaryViewOnly"),
        ("catalog.resources.manage", "PrimaryManage"),
    };
}

[DeveloperMigration(2026, 10, 06, Developer.SilvioHabazin, 1)]
public class SeedCatalogResourcesCapability : DuneLightMigration
{
    public override void Up()
    {
        Guid capabilityId = ResourcesCapabilitySeedData.CapabilityId();

        Insert.IntoTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = capabilityId,
            key = ResourcesCapabilitySeedData.CapabilityKey,
            version = ResourcesCapabilitySeedData.CapabilityVersion,
            category_key = "catalog",
            scope_model = "ViewManage",
            sensitivity = "Normal",
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        });

        foreach ((string grantKey, string role) in ResourcesCapabilitySeedData.Grants)
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

/// <summary>Admin v4 = Admin v3 (Admin v2 selekcije + organization.permissions.manage = Manage, vidi SeedAdminTemplateV3)
/// + JEDNA nova selekcija (catalog.resources.manage = Manage) + isti compatibility extras.</summary>
[DeveloperMigration(2026, 10, 06, Developer.SilvioHabazin, 2)]
public class SeedAdminTemplateV4 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid templateId = ResourcesCapabilitySeedData.AdminTemplateId();

        CapabilityV1SeedData.TemplateSeed adminV2 = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin");

        Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = templateId,
            key = "admin",
            version = ResourcesCapabilitySeedData.AdminTemplateVersion,
            display_name_hr = adminV2.DisplayNameHr,
            is_active = true,
            created_at = now
        });

        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in adminV2.Selections)
            InsertSelection(templateId, CapabilityV1SeedData.CapabilityId(selection.CapabilityKey), selection.SelectedScope);

        // v3 dodatak (organization.permissions.manage) i v4 dodatak (catalog.resources.manage).
        InsertSelection(templateId, PermissionAdministrationCapabilitySeedData.CapabilityId(), "Manage");
        InsertSelection(templateId, ResourcesCapabilitySeedData.CapabilityId(), "Manage");

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
