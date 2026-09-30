using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Room capacity + Resource katalog (temelj za buduće segmente termina; zakazivanje ih još NE koristi).
///
/// 1. rooms.capacity — maksimalan broj OSOBA istovremeno u prostoriji, NOT NULL, CHECK (capacity &gt;= 1).
///    Postojeći (razvojni/testni) redovi dobivaju TEHNIČKU vrijednost 1 isključivo zato da stupac može biti NOT NULL;
///    to NIJE izvedeno iz allow_concurrent_bookings (zastavica ne nosi brojčanu informaciju, a i "ekskluzivna"
///    prostorija može primiti grupni termin s više osoba). Zadana vrijednost se odmah uklanja — svako kreiranje
///    prostorije mora eksplicitno zadati kapacitet. allow_concurrent_bookings ostaje netaknut (legacy, i dalje
///    određuje trenutno pravilo preklapanja termina).
/// 2. resources — generički resurs poslovnice (oprema/mjesta s kapacitetom), isti oblik i konvencije kao rooms:
///    FK na organizations i companies, indeks (organization_id, company_id), CHECK (capacity &gt;= 1) i djelomični
///    unique indeks aktivnog normaliziranog naziva po (organization_id, company_id) — isto kao ux_rooms_org_company_name_active.
/// </summary>
[DeveloperMigration(2026, 10, 06, Developer.SilvioHabazin, 0)]
public class RoomCapacityAndResources : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Rooms} ADD COLUMN capacity integer NOT NULL DEFAULT 1;
            ALTER TABLE dunelight.{Tables.Rooms} ALTER COLUMN capacity DROP DEFAULT;
            ALTER TABLE dunelight.{Tables.Rooms} ADD CONSTRAINT ck_rooms_capacity_positive CHECK (capacity >= 1);");

        Create.Table(Tables.Resources)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_resources")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("capacity").AsInt32().NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_resources_organization_id")
            .FromTable(Tables.Resources).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_resources_company_id")
            .FromTable(Tables.Resources).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_resources_organization_company")
            .OnTable(Tables.Resources).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending();

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Resources} ADD CONSTRAINT ck_resources_capacity_positive CHECK (capacity >= 1);
            CREATE UNIQUE INDEX ux_resources_org_company_name_active
                ON dunelight.{Tables.Resources} (organization_id, company_id, lower(trim(name))) WHERE is_active = true;");
    }

    public override void Down()
    {
        Delete.Table(Tables.Resources).InSchema(Tables.Schemas.DuneLight);

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Rooms} DROP CONSTRAINT ck_rooms_capacity_positive;
            ALTER TABLE dunelight.{Tables.Rooms} DROP COLUMN capacity;");
    }
}
