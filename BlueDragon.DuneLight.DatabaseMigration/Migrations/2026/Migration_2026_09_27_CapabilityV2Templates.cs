using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// FAZA 3 — Template-version-upgrade sustav v2 (vidi CapabilityV2SeedData). Seeda TRI nova DefaultRoleTemplate
/// retka (Admin/Trener/Recepcija, Version=2, IsActive=true) + njihove DefaultRoleTemplateCapability retke
/// (referencirajući POSTOJEĆE v1 CapabilityDefinition id-eve, CapabilityV1SeedData.CapabilityId) + Admin v2
/// DefaultRoleTemplateGrant compatibility-extra retke.
///
/// KRITIČNI INVARIJANT (Part U/X) — OVA MIGRACIJA NE PIŠE, NE MIJENJA I NE BRIŠE NIŠTA U grant_groups,
/// grant_group_grants, grant_group_capability_snapshots NITI grant_group_template_grants. Objavljivanje nove
/// predložak-verzije je ČISTO METAPODATKOVNO — nijedan postojeći tenant GrantGroup se ne mijenja dok Owner
/// eksplicitno ne pokrene upgrade-apply tok (vidi GrantGroupTemplateUpgradeService.Apply). Nova v1->v2 IsActive
/// tranzicija (v2 postaje "latest active", v1 ostaje čitljiv preko DefaultRoleTemplateHandler.GetByKey(key,
/// exactVersion) za BASE razrješavanje u three-way merge algoritmu) je jedina promjena vidljiva izvan ove tri
/// tablice — a ta promjena utječe SAMO na "koji je predložak najnoviji", ne na postojeće tenant podatke.
/// </summary>
[DeveloperMigration(2026, 09, 27, Developer.SilvioHabazin, 0)]
public class SeedDefaultRoleTemplatesV2 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (CapabilityV1SeedData.TemplateSeed template in CapabilityV2SeedData.Templates)
        {
            Guid templateId = CapabilityV2SeedData.TemplateId(template.Key);

            Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = templateId,
                key = template.Key,
                version = CapabilityV2SeedData.TemplateVersion,
                display_name_hr = template.DisplayNameHr,
                is_active = true,
                created_at = now
            });

            foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in template.Selections)
            {
                Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
                {
                    id = Guid.NewGuid(),
                    default_role_template_id = templateId,
                    capability_definition_id = CapabilityV1SeedData.CapabilityId(selection.CapabilityKey),
                    selected_scope = selection.SelectedScope
                });
            }

            foreach (string grantKey in template.CompatibilityExtraGrants)
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

        // NAMJERNO nema koraka koji dira grant_groups/grant_group_grants/grant_group_capability_snapshots/
        // grant_group_template_grants — vidi klasnu napomenu iznad. Postojeći tenanti ostaju TOČNO na svojoj v1
        // (ili custom) provenance dok Owner ne odobri upgrade preko novog review/apply toka.
    }
}

/// <summary>FAZA 3 — nova audit tablica za primijenjene template-upgrade odluke (vidi
/// GrantGroupTemplateUpgradeAuditLog entitet / GrantGroupTemplateUpgradeService.Apply). Redak se upisuje ATOMSKI,
/// u ISTOJ transakciji kao snapshot/provenance/raw-grant promjene — vidi klasnu napomenu na
/// GrantGroupTemplateUpgradeAuditLogHandler zašto se (za razliku od OrganizationBrandingAuditLogHandler) ne
/// koristi zaseban DbContext za taj upis.</summary>
[DeveloperMigration(2026, 09, 27, Developer.SilvioHabazin, 1)]
public class CreateGrantGroupTemplateUpgradeAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GrantGroupTemplateUpgradeAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_grant_group_template_upgrade_audit_log")
            .WithColumn("grant_group_id").AsGuid().NotNullable()
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("source_template_key").AsString(150).NotNullable()
            .WithColumn("source_template_version").AsInt32().NotNullable()
            .WithColumn("target_template_key").AsString(150).NotNullable()
            .WithColumn("target_template_version").AsInt32().NotNullable()
            .WithColumn("capability_changes_json").AsCustom("text").NotNullable()
            .WithColumn("conflict_resolutions_json").AsCustom("text").NotNullable()
            .WithColumn("applied_at").AsDateTimeOffset().NotNullable()
            .WithColumn("applied_by").AsGuid().Nullable();

        Create.ForeignKey("fk_grant_group_template_upgrade_audit_log_grant_group_id")
            .FromTable(Tables.GrantGroupTemplateUpgradeAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("grant_group_id")
            .ToTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_grant_group_template_upgrade_audit_log_grant_group_id")
            .OnTable(Tables.GrantGroupTemplateUpgradeAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("grant_group_id").Ascending();
    }
}
