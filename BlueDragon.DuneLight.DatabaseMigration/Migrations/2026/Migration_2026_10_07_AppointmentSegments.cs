using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D1 — segmenti termina kao ADDITIVNA ciljna struktura (nije autoritativna, nema backfilla, nema dual-writea,
/// nema triggera). Stupci appointments tablice (service_id, employee_id, room_id, starts_at, duration_minutes) se ne
/// diraju i zakazivanje ih i dalje koristi.
///
/// appointment_segments — jedno izvođenje jedne usluge unutar termina. company_id se namjerno NE duplicira (izvodi se
/// preko appointments.company_id). Vremena su timestamptz (UTC instanti). CHECK: planned_end &gt; planned_start;
/// actual_end ne postoji bez actual_start i actual_end &gt;= actual_start. Brisanje termina kaskadno briše segmente (isti
/// obrazac kao bookings); usluga/prostorija su Restrict.
/// appointment_segment_employees — PK (appointment_segment_id, employee_id); nula ili više zaposlenika po segmentu.
/// appointment_segment_resources — PK (appointment_segment_id, resource_id), quantity_required &gt; 0.
/// </summary>
[DeveloperMigration(2026, 10, 07, Developer.SilvioHabazin, 0)]
public class AppointmentSegments : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.AppointmentSegments)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_appointment_segments")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("planned_start").AsDateTimeOffset().NotNullable()
            .WithColumn("planned_end").AsDateTimeOffset().NotNullable()
            .WithColumn("actual_start").AsDateTimeOffset().Nullable()
            .WithColumn("actual_end").AsDateTimeOffset().Nullable()
            .WithColumn("room_id").AsGuid().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable();

        Create.ForeignKey("fk_appointment_segments_organization_id")
            .FromTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointment_segments_appointment_id")
            .FromTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_appointment_segments_service_id")
            .FromTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_appointment_segments_room_id")
            .FromTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("room_id")
            .ToTable(Tables.Rooms).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_appointment_segments_appointment_id")
            .OnTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending();

        Create.Index("ix_appointment_segments_organization_planned")
            .OnTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("planned_start").Ascending()
            .OnColumn("planned_end").Ascending();

        Create.Index("ix_appointment_segments_service_id")
            .OnTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("service_id").Ascending();

        Create.Index("ix_appointment_segments_room_planned")
            .OnTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("room_id").Ascending()
            .OnColumn("planned_start").Ascending()
            .OnColumn("planned_end").Ascending();

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.AppointmentSegments}
                ADD CONSTRAINT ck_appointment_segments_planned_range CHECK (planned_end > planned_start),
                ADD CONSTRAINT ck_appointment_segments_actual_range
                    CHECK (actual_end IS NULL OR (actual_start IS NOT NULL AND actual_end >= actual_start));");

        Create.Table(Tables.AppointmentSegmentEmployees)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("appointment_segment_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable();

        Create.PrimaryKey("pk_appointment_segment_employees")
            .OnTable(Tables.AppointmentSegmentEmployees).WithSchema(Tables.Schemas.DuneLight)
            .Columns("appointment_segment_id", "employee_id");

        Create.ForeignKey("fk_appointment_segment_employees_appointment_segment_id")
            .FromTable(Tables.AppointmentSegmentEmployees).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_segment_id")
            .ToTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_appointment_segment_employees_employee_id")
            .FromTable(Tables.AppointmentSegmentEmployees).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_appointment_segment_employees_employee_id")
            .OnTable(Tables.AppointmentSegmentEmployees).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("employee_id").Ascending();

        Create.Table(Tables.AppointmentSegmentResources)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("appointment_segment_id").AsGuid().NotNullable()
            .WithColumn("resource_id").AsGuid().NotNullable()
            .WithColumn("quantity_required").AsInt32().NotNullable();

        Create.PrimaryKey("pk_appointment_segment_resources")
            .OnTable(Tables.AppointmentSegmentResources).WithSchema(Tables.Schemas.DuneLight)
            .Columns("appointment_segment_id", "resource_id");

        Create.ForeignKey("fk_appointment_segment_resources_appointment_segment_id")
            .FromTable(Tables.AppointmentSegmentResources).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_segment_id")
            .ToTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_appointment_segment_resources_resource_id")
            .FromTable(Tables.AppointmentSegmentResources).InSchema(Tables.Schemas.DuneLight).ForeignColumn("resource_id")
            .ToTable(Tables.Resources).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_appointment_segment_resources_resource_id")
            .OnTable(Tables.AppointmentSegmentResources).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("resource_id").Ascending();

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.AppointmentSegmentResources}
                ADD CONSTRAINT ck_appointment_segment_resources_quantity_positive CHECK (quantity_required > 0);");
    }

    public override void Down()
    {
        Delete.Table(Tables.AppointmentSegmentResources).InSchema(Tables.Schemas.DuneLight);
        Delete.Table(Tables.AppointmentSegmentEmployees).InSchema(Tables.Schemas.DuneLight);
        Delete.Table(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight);
    }
}
