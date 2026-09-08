using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Prostorije poslovnice (npr. "Masaža", "Vježbanje 1") — dodjeljuju se opcionalno na termin
/// (appointments.room_id) i kao prijedlog na grupu (groups.default_room_id, isti obrazac kao
/// default_trainer_id). allow_concurrent_bookings=false (zadano) znači da sustav tvrdo blokira
/// preklapajuće termine u istoj prostoriji; true dopušta da više termina dijeli istu prostoriju.
/// </summary>
[DeveloperMigration(2026, 09, 01, Developer.SilvioHabazin, 0)]
public class CreateRoomsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Rooms)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_rooms")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("allow_concurrent_bookings").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_rooms_organization_id")
            .FromTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_rooms_company_id")
            .FromTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_rooms_organization_company")
            .OnTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending();

        Alter.Table(Tables.Appointments)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("room_id").AsGuid().Nullable();

        Create.ForeignKey("fk_appointments_room_id")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("room_id")
            .ToTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Alter.Table(Tables.Groups)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("default_room_id").AsGuid().Nullable();

        Create.ForeignKey("fk_groups_default_room_id")
            .FromTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("default_room_id")
            .ToTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");
    }
}
