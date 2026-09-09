using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Audit log za promjene organizacijskog brandinga (boje, logo, favicon) — vidi OrganizationBrandingService.
/// Bilježi tko je i kada promijenio što, bez sadržaja datoteka.
/// </summary>
[DeveloperMigration(2026, 09, 09, Developer.SilvioHabazin, 0)]
public class CreateOrganizationBrandingAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.OrganizationBrandingAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_organization_branding_audit_log")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(30).NotNullable()
            .WithColumn("old_value").AsString(511).Nullable()
            .WithColumn("new_value").AsString(511).Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        Create.ForeignKey("fk_organization_branding_audit_log_organization_id")
            .FromTable(Tables.OrganizationBrandingAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_organization_branding_audit_log_organization_id")
            .OnTable(Tables.OrganizationBrandingAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending();
    }
}
