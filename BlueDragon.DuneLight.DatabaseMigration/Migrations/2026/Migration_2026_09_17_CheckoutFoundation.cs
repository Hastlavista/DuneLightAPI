using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi Checkout/CheckoutItem/PaymentAllocation POS temelje (vidi Checkout.cs, Infrastructure, za punu
/// domensku napomenu) i seli Payment vlasništvo s Bookinga na Checkout — jedan Payment sad može namiriti više
/// komercijalnih stavki odjednom preko PaymentAllocation, umjesto izravnog Payment.booking_id.
/// </summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 0)]
public class CreateCheckoutsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Checkouts)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_checkouts")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("completed_at").AsDateTimeOffset().Nullable()
            .WithColumn("completed_by").AsGuid().Nullable()
            .WithColumn("cancelled_at").AsDateTimeOffset().Nullable()
            .WithColumn("cancelled_by").AsGuid().Nullable();

        Create.ForeignKey("fk_checkouts_organization_id")
            .FromTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_checkouts_company_id")
            .FromTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_checkouts_client_id")
            .FromTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_checkouts_org_client")
            .OnTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("client_id").Ascending();

        Create.Index("ix_checkouts_org_company_status")
            .OnTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending()
            .OnColumn("status").Ascending();
    }
}

/// <summary>
/// checkout_items.booking_id/package_id: točno JEDAN mora biti popunjen, prema type (CHECK constraint ispod,
/// isti obrazac kao ostale "vlasnik je točno jedno od X/Y" situacije u shemi — vidi PriceListItem.ServiceId/
/// PackageId). locks_booking djelomični unique indeks sprječava isti Booking istovremeno u dva Open checkouta
/// (vidi CheckoutItem.LocksBooking domensku napomenu).
/// </summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 1)]
public class CreateCheckoutItemsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CheckoutItems)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_checkout_items")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("checkout_id").AsGuid().NotNullable()
            .WithColumn("type").AsString(20).NotNullable()
            .WithColumn("description").AsCustom("text").Nullable()
            .WithColumn("unit_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("quantity").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("booking_id").AsGuid().Nullable()
            .WithColumn("package_id").AsGuid().Nullable()
            .WithColumn("client_package_id").AsGuid().Nullable()
            .WithColumn("locks_booking").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.ForeignKey("fk_checkout_items_checkout_id")
            .FromTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_id")
            .ToTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_checkout_items_booking_id")
            .FromTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_checkout_items_package_id")
            .FromTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("package_id")
            .ToTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_checkout_items_client_package_id")
            .FromTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_package_id")
            .ToTable(Tables.ClientPackages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_checkout_items_org_checkout")
            .OnTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("checkout_id").Ascending();

        Execute.Sql(@"
            ALTER TABLE dunelight.checkout_items
            ADD CONSTRAINT ck_checkout_items_subject CHECK (
                (type = 'Booking' AND booking_id IS NOT NULL AND package_id IS NULL) OR
                (type = 'Package' AND package_id IS NOT NULL AND booking_id IS NULL)
            );");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_checkout_items_locks_booking " +
            "ON dunelight.checkout_items (booking_id) WHERE locks_booking = true;");
    }
}

[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 2)]
public class CreatePaymentAllocationsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PaymentAllocations)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_payment_allocations")
            .WithColumn("payment_id").AsGuid().NotNullable()
            .WithColumn("checkout_item_id").AsGuid().NotNullable()
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_payment_allocations_payment_id")
            .FromTable(Tables.PaymentAllocations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("payment_id")
            .ToTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_payment_allocations_checkout_item_id")
            .FromTable(Tables.PaymentAllocations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_item_id")
            .ToTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_payment_allocations_payment_id")
            .OnTable(Tables.PaymentAllocations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("payment_id").Ascending();

        Create.Index("ix_payment_allocations_checkout_item_id")
            .OnTable(Tables.PaymentAllocations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("checkout_item_id").Ascending();
    }
}

[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 3)]
public class CreateCheckoutAuditLogTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CheckoutAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_checkout_audit_log")
            .WithColumn("checkout_id").AsGuid().NotNullable()
            .WithColumn("change_type").AsString(30).NotNullable()
            .WithColumn("old_value").AsCustom("text").Nullable()
            .WithColumn("new_value").AsCustom("text").Nullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        Create.ForeignKey("fk_checkout_audit_log_checkout_id")
            .FromTable(Tables.CheckoutAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_id")
            .ToTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_checkout_audit_log_checkout_id")
            .OnTable(Tables.CheckoutAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("checkout_id").Ascending();
    }
}

/// <summary>Nullable za sad — backfill (MigrateBookingPaymentsToCheckouts* niže) ga popunjava prije nego što
/// postane NotNullable u FinalizePaymentsCheckoutOwnership.</summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 4)]
public class AddCheckoutIdToPayments : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Payments)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("checkout_id").AsGuid().Nullable();
    }
}

/// <summary>
/// Za svaki Booking koji ima barem jedan Payment, stvara TOČNO JEDAN historijski Checkout (vidi spec section
/// 53) — Status ovisi o tome je li booking u trenutku migracije već u potpunosti podmiren (zbroj Completed
/// Paymenta &gt;= Amount, gratis, ili paket-namiren): takav postaje Completed (immutable povijest), inače
/// ostaje Open (stari djelomično plaćen booking i dalje prima uplate kroz novi Checkout API). Id je
/// deterministički (md5 hash Booking.Id-a) da ga sljedeći koraci mogu neovisno rekonstruirati bez privremene
/// mapping tablice.
/// </summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 5)]
public class MigrateBookingPaymentsToCheckouts : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.checkouts
                (id, organization_id, company_id, client_id, status, created_at, created_by, completed_at, completed_by)
            SELECT
                md5(b.id::text || ':checkout')::uuid,
                b.organization_id,
                a.company_id,
                b.client_id,
                CASE WHEN pa.paid_amount >= b.amount OR b.amount <= 0
                          OR (b.client_package_id IS NOT NULL AND b.package_coverage_applied AND NOT b.package_coverage_returned)
                     THEN 'Completed' ELSE 'Open' END,
                pa.first_payment_at,
                NULL,
                CASE WHEN pa.paid_amount >= b.amount OR b.amount <= 0
                          OR (b.client_package_id IS NOT NULL AND b.package_coverage_applied AND NOT b.package_coverage_returned)
                     THEN pa.last_payment_at ELSE NULL END,
                NULL
            FROM dunelight.bookings b
            JOIN dunelight.appointments a ON a.id = b.appointment_id
            JOIN (
                SELECT booking_id, MIN(created_at) AS first_payment_at, MAX(created_at) AS last_payment_at,
                       SUM(CASE WHEN status = 'Completed' THEN amount ELSE 0 END) AS paid_amount
                FROM dunelight.payments
                GROUP BY booking_id
            ) pa ON pa.booking_id = b.id;");
    }
}

/// <summary>Jedna CheckoutItem (Type=Booking) po historijskom Checkoutu stvorenom gore — snapshotta trenutan
/// Booking.Amount/naziv usluge. locks_booking prati je li roditeljski Checkout ostao Open (vidi gore).</summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 6)]
public class MigrateBookingPaymentsToCheckoutItems : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.checkout_items
                (id, organization_id, checkout_id, type, description, unit_price, quantity, amount,
                 booking_id, package_id, client_package_id, locks_booking, created_at, created_by)
            SELECT
                md5(b.id::text || ':checkout-item')::uuid,
                b.organization_id,
                c.id,
                'Booking',
                COALESCE(s.name, 'Booking'),
                b.amount,
                1,
                b.amount,
                b.id,
                NULL,
                NULL,
                (c.status = 'Open'),
                c.created_at,
                NULL
            FROM dunelight.bookings b
            JOIN dunelight.appointments a ON a.id = b.appointment_id
            LEFT JOIN dunelight.services s ON s.id = a.service_id
            JOIN dunelight.checkouts c ON c.id = md5(b.id::text || ':checkout')::uuid
            WHERE EXISTS (SELECT 1 FROM dunelight.payments p WHERE p.booking_id = b.id);");
    }
}

/// <summary>Jedna PaymentAllocation po postojećem Payment retku, puni Amount (staro 1:1 Payment-Booking
/// odgovara točno jednoj stavci, nema potrebe za split) — i konačno povezuje postojeći Payment na svoj novi
/// Checkout preko checkout_id (booking_id se briše tek u FinalizePaymentsCheckoutOwnership ispod).</summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 7)]
public class MigrateBookingPaymentsToPaymentAllocations : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.payment_allocations (id, payment_id, checkout_item_id, amount, created_at)
            SELECT
                md5(p.id::text || ':allocation')::uuid,
                p.id,
                md5(p.booking_id::text || ':checkout-item')::uuid,
                p.amount,
                p.created_at
            FROM dunelight.payments p
            WHERE p.booking_id IS NOT NULL;");

        Execute.Sql(@"
            UPDATE dunelight.payments
            SET checkout_id = md5(booking_id::text || ':checkout')::uuid
            WHERE booking_id IS NOT NULL;");
    }
}

/// <summary>Zaključuje seobu vlasništva — checkout_id postaje obavezan/FK-om zaštićen, staro booking_id (i
/// njegov FK/indeks) se uklanja. Razvojna faza dopušta breaking migraciju (vidi spec), podaci su već preneseni
/// u koracima gore, ne bacaju se.</summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 8)]
public class FinalizePaymentsCheckoutOwnership : DuneLightMigration
{
    public override void Up()
    {
        Delete.ForeignKey("fk_payments_booking_id").OnTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight);
        Delete.Index("ix_payments_org_booking").OnTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("booking_id").FromTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight);

        Alter.Table(Tables.Payments)
            .InSchema(Tables.Schemas.DuneLight)
            .AlterColumn("checkout_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_payments_checkout_id")
            .FromTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_id")
            .ToTable(Tables.Checkouts).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_payments_org_checkout")
            .OnTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("checkout_id").Ascending();
    }
}

/// <summary>
/// Podržava usku Completed -&gt; Voided internu korekcijsku tranziciju (vidi CheckoutStatus.Voided) — status
/// ostaje AsString(20), "Voided" staje bez proširenja stupca. Odvojeno od cancelled_at/by (Cancel je redovna
/// POS operacija na Open checkoutu, Voided je administrativna korekcija Completed checkouta).
/// </summary>
[DeveloperMigration(2026, 09, 17, Developer.SilvioHabazin, 9)]
public class AddVoidedMetadataToCheckouts : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Checkouts)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("voided_at").AsDateTimeOffset().Nullable()
            .AddColumn("voided_by").AsGuid().Nullable()
            .AddColumn("void_reason").AsCustom("text").Nullable();
    }
}
