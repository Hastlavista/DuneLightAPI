using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi Payment ledger (vidi Payment.cs, Infrastructure) — zamjenjuje Booking.payment_method/is_paid kao
/// izvor istine za "je li i koliko plaćeno". Booking i dalje nosi komercijalnu obvezu (amount); stvarno
/// primljen novac sad živi u zasebnoj payments tablici, s podrškom za partial/split (više Paymenta po
/// Bookingu) i Void (poništenje pogreške bez brisanja povijesti).
/// </summary>
[DeveloperMigration(2026, 09, 16, Developer.SilvioHabazin, 0)]
public class CreatePaymentsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Payments)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_payments")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("booking_id").AsGuid().NotNullable()
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("method").AsString(20).NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("is_checkin_generated").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("voided_at").AsDateTimeOffset().Nullable()
            .WithColumn("voided_by").AsGuid().Nullable()
            .WithColumn("void_reason").AsCustom("text").Nullable();

        Create.ForeignKey("fk_payments_organization_id")
            .FromTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_payments_booking_id")
            .FromTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ix_payments_org_booking")
            .OnTable(Tables.Payments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("booking_id").Ascending();
    }
}

/// <summary>
/// Prebacuje postojeće naplaćene Bookinge u stvaran Payment redak. SAMO kad je is_paid=true I
/// payment_method je stvarna novčana metoda (NE 'Package' — paket-pokriće nikad nije bio novac, ne smije
/// postati lažan Payment) I amount &gt; 0 (0-iznos je besplatan termin, ne treba Payment). Nepodmiren
/// (is_paid=false) ili paket-pokriven Booking ne dobiva nijedan Payment redak — vidi Payment.cs klasnu
/// napomenu. Historijski created_at koristi najbolji dostupan trag: Booking.updated_at (trenutak zadnje
/// promjene, obično upravo naplata/check-in) ako postoji, inače Booking.created_at — točno vrijeme same
/// povijesne naplate nije bilo zasebno bilježeno prije ovog zahvata (vidi spec section 47, ograničenje
/// dokumentirano ovdje jer preciznije nije moguće rekonstruirati).
/// </summary>
[DeveloperMigration(2026, 09, 16, Developer.SilvioHabazin, 1)]
public class MigrateBookingPaymentsToPayments : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.payments
                (id, organization_id, booking_id, amount, method, status, note, is_checkin_generated, created_at, created_by)
            SELECT
                (md5(random()::text || clock_timestamp()::text || b.id::text))::uuid,
                b.organization_id, b.id, b.amount, b.payment_method, 'Completed', NULL, false,
                COALESCE(b.updated_at, b.created_at), b.updated_by
            FROM dunelight.bookings b
            WHERE b.is_paid = true
              AND b.payment_method IS NOT NULL
              AND b.payment_method <> 'Package'
              AND b.amount > 0;");
    }
}

/// <summary>Uklanja obsoletne stupce tek NAKON migracije podataka iznad — razvojna faza dopušta breaking
/// migraciju (vidi spec), podaci se svejedno čuvaju/prenose u payments, ne bacaju.</summary>
[DeveloperMigration(2026, 09, 16, Developer.SilvioHabazin, 2)]
public class DropPaymentMethodAndIsPaidFromBookings : DuneLightMigration
{
    public override void Up()
    {
        Delete.Column("payment_method")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("is_paid")
            .FromTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight);
    }
}
