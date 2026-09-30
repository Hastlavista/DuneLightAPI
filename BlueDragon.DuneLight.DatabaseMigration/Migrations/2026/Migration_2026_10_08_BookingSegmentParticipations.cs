using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D2 — booking_segment_participations kao ADDITIVNA ciljna struktura (nije autoritativna; nema backfilla,
/// dual-writea ni triggera; bookings/appointments/checkout/paketi se ne diraju).
///
/// - FK booking_id i appointment_segment_id su RESTRICT (NO ACTION): povijest sudjelovanja se nikad ne briše kaskadom.
///   Posljedica: brisanje termina (koji kaskadno briše bookinge i segmente) je blokirano čim postoji sudjelovanje;
///   bez sudjelovanja se ništa ne mijenja.
/// - UNIQUE (booking_id, appointment_segment_id).
/// - "Booking i segment pripadaju istom terminu" NIJE izraženo u bazi: zahtijevalo bi redundantni appointment_id na
///   sudjelovanju i dodatne unique ključeve (id, appointment_id) na bookings i appointment_segments samo radi
///   složenih FK-ova. Pravilo provodi jedina write-putanja (BookingSegmentParticipationHandler.Add); appointment_id
///   Bookinga i segmenta se nikad ne mijenja, pa provjera nije podložna utrci.
/// - status: samo Confirmed/Completed/Cancelled/NoShow (CHECK); status_version &gt;= 0.
/// - arrived_by bez arrived_at nije dopušten; *_by stupci nemaju FK na users (projektna konvencija).
/// - Cijene numeric(10,2) kao bookings.amount; base/suggested/amount &gt;= 0, adjustment_amount smije biti negativan.
/// </summary>
[DeveloperMigration(2026, 10, 08, Developer.SilvioHabazin, 0)]
public class BookingSegmentParticipations : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.BookingSegmentParticipations)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_booking_segment_participations")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("booking_id").AsGuid().NotNullable()
            .WithColumn("appointment_segment_id").AsGuid().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("status_version").AsInt32().NotNullable()
            .WithColumn("arrived_at").AsDateTimeOffset().Nullable()
            .WithColumn("arrived_by").AsGuid().Nullable()
            .WithColumn("cancellation_reason").AsCustom("text").Nullable()
            .WithColumn("is_late_cancellation").AsBoolean().Nullable()
            .WithColumn("base_amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("base_amount_source").AsString(20).NotNullable()
            .WithColumn("adjustment_amount").AsDecimal(10, 2).Nullable()
            .WithColumn("suggested_amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("is_amount_manually_overridden").AsBoolean().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable();

        Create.ForeignKey("fk_booking_segment_participations_organization_id")
            .FromTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_booking_segment_participations_booking_id")
            .FromTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_booking_segment_participations_appointment_segment_id")
            .FromTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_segment_id")
            .ToTable(Tables.AppointmentSegments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Po bookingu: pokriveno vodećim stupcem jedinstvenog indeksa. Po segmentu (buduća popunjenost prostorije /
        // sudionici segmenta): zaseban indeks.
        Create.Index("ux_booking_segment_participations_booking_segment")
            .OnTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("booking_id").Ascending()
            .OnColumn("appointment_segment_id").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_booking_segment_participations_appointment_segment_id")
            .OnTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_segment_id").Ascending();

        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.BookingSegmentParticipations}
                ADD CONSTRAINT ck_booking_segment_participations_status
                    CHECK (status IN ('Confirmed', 'Completed', 'Cancelled', 'NoShow')),
                ADD CONSTRAINT ck_booking_segment_participations_status_version CHECK (status_version >= 0),
                ADD CONSTRAINT ck_booking_segment_participations_arrival CHECK (arrived_by IS NULL OR arrived_at IS NOT NULL),
                ADD CONSTRAINT ck_booking_segment_participations_base_amount_source
                    CHECK (base_amount_source IN ('CompanySpecific', 'AllCompanies', 'Default')),
                ADD CONSTRAINT ck_booking_segment_participations_amounts_non_negative
                    CHECK (base_amount >= 0 AND suggested_amount >= 0 AND amount >= 0);");
    }

    public override void Down()
    {
        Delete.Table(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight);
    }
}
