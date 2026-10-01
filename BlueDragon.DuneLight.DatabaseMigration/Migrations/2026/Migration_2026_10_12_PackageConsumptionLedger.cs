using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase D3B3A — potrošnja paketa postaje POVIJEST NA SUDJELOVANJU (package_consumptions ledger) umjesto promjenjivih
/// zastavica na Bookingu.
///
/// 1. package_consumptions: jedan redak = entitlement jednog client_package-a primijenjen na jedno sudjelovanje za jednu
///    uslugu (units 1 = skinut jedan ulazak, 0 = neograničen paket). Consumed -&gt; Reversed na istom retku (reversed_at/by +
///    reversal_reason), nikad brisanje. FK prema organizaciji, paketu, sudjelovanju i usluzi su RESTRICT (povijest se ne
///    briše kaskadom). Djelomični unique indeks: najviše JEDNA aktivna (Consumed) potrošnja po sudjelovanju — ponovljen
///    completion ne skida dvaput. Indeksi po paketu i po sudjelovanju.
/// 2. organization_settings.package_consumption_timing (NOT NULL, default OnCompletion — jedino trenutno podržano
///    ponašanje, stvarna domenska vrijednost a ne skrivanje nepotpunog upisa).
/// 3. bookings.client_package_id / coverage_type / package_coverage_applied / package_coverage_returned(_at/_by) se
///    UKLANJAJU — bez backfilla (razvojna baza, sadržaj se ne čuva; vidi politiku razvojne baze). CoverageType i
///    "applied/returned" se od sada izvode iz povijesti potrošnje (Utils.PackageConsumptions).
///
/// Down: vraća stupce Bookinga (prazne, isti tip/default) i uklanja ledger i postavku — povijest potrošnje se gubi.
/// </summary>
[DeveloperMigration(2026, 10, 12, Developer.SilvioHabazin, 0)]
public class PackageConsumptionLedger : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PackageConsumptions)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_package_consumptions")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("client_package_id").AsGuid().NotNullable()
            .WithColumn("booking_segment_participation_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("units").AsInt32().NotNullable()
            .WithColumn("service_starts_at").AsDateTimeOffset().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("reversed_at").AsDateTimeOffset().Nullable()
            .WithColumn("reversed_by").AsGuid().Nullable()
            .WithColumn("reversal_reason").AsString(30).Nullable();

        Create.ForeignKey("fk_package_consumptions_organization_id")
            .FromTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");
        Create.ForeignKey("fk_package_consumptions_client_package_id")
            .FromTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");
        Create.ForeignKey("fk_package_consumptions_participation_id")
            .FromTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_segment_participation_id")
            .ToTable(Tables.BookingSegmentParticipations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");
        Create.ForeignKey("fk_package_consumptions_service_id")
            .FromTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_package_consumptions_client_package_id")
            .OnTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("client_package_id").Ascending();
        Create.Index("ix_package_consumptions_participation_id")
            .OnTable(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("booking_segment_participation_id").Ascending();

        Execute.Sql($@"
            CREATE UNIQUE INDEX ux_package_consumptions_active_participation
                ON dunelight.{Tables.PackageConsumptions} (booking_segment_participation_id)
                WHERE status = 'Consumed';

            ALTER TABLE dunelight.{Tables.PackageConsumptions}
                ADD CONSTRAINT ck_package_consumptions_status CHECK (status IN ('Consumed', 'Reversed')),
                ADD CONSTRAINT ck_package_consumptions_units CHECK (units IN (0, 1)),
                ADD CONSTRAINT ck_package_consumptions_reversal_reason
                    CHECK (reversal_reason IS NULL OR reversal_reason IN ('Cancellation', 'NoShow', 'CompletionCorrection')),
                ADD CONSTRAINT ck_package_consumptions_reversal CHECK (
                    (status = 'Consumed' AND reversed_at IS NULL AND reversed_by IS NULL AND reversal_reason IS NULL) OR
                    (status = 'Reversed' AND reversed_at IS NOT NULL AND reversal_reason IS NOT NULL));

            ALTER TABLE dunelight.{Tables.OrganizationSettings}
                ADD COLUMN package_consumption_timing varchar(30) NOT NULL DEFAULT 'OnCompletion',
                ADD CONSTRAINT ck_organization_settings_package_consumption_timing
                    CHECK (package_consumption_timing IN ('OnCompletion'));

            ALTER TABLE dunelight.{Tables.Bookings}
                DROP COLUMN client_package_id,
                DROP COLUMN coverage_type,
                DROP COLUMN package_coverage_applied,
                DROP COLUMN package_coverage_returned,
                DROP COLUMN package_coverage_returned_at,
                DROP COLUMN package_coverage_returned_by;");
    }

    public override void Down()
    {
        Execute.Sql($@"
            ALTER TABLE dunelight.{Tables.Bookings}
                ADD COLUMN client_package_id uuid NULL,
                ADD COLUMN coverage_type varchar(20) NULL,
                ADD COLUMN package_coverage_applied boolean NOT NULL DEFAULT false,
                ADD COLUMN package_coverage_returned boolean NOT NULL DEFAULT false,
                ADD COLUMN package_coverage_returned_at timestamptz NULL,
                ADD COLUMN package_coverage_returned_by uuid NULL,
                ADD CONSTRAINT fk_bookings_client_package_id FOREIGN KEY (client_package_id) REFERENCES dunelight.{Tables.ClientPackages} (id);

            ALTER TABLE dunelight.{Tables.OrganizationSettings}
                DROP CONSTRAINT ck_organization_settings_package_consumption_timing,
                DROP COLUMN package_consumption_timing;");

        Delete.Table(Tables.PackageConsumptions).InSchema(Tables.Schemas.DuneLight);
    }
}
