using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Premješta komercijalno stanje (Amount/SuggestedAmount/IsAmountManuallyOverridden/PaymentMethod/IsPaid)
/// s Appointment na Booking — omogućuje mješovito plaćanje po klijentu na istom terminu (npr. duo: jedan
/// klijent iz paketa, drugi karticom). Vidi Booking.cs (Infrastructure) za punu domensku napomenu.
///
/// Appointment.amount je prije ovoga bio PO-KLIJENTU cijena usluge (resolved preko IPricingService iz
/// Service/Company/Date, neovisno o broju klijenata na terminu), ne ukupan iznos termina — potvrđeno u
/// AppointmentService (ResolveSuggestedAmount ne prima broj klijenata). Migracija zato kopira postojeći
/// appointment-razina iznos IDENTIČNO na svaki Booking tog termina (ne dijeli ga) — to je ispravno
/// tumačenje postojećih podataka, ne umnožavanje povijesne vrijednosti (vidi spec section 21/22).
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 13)]
public class AddCommercialColumnsToBookings : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Bookings)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("amount").AsDecimal(10, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("suggested_amount").AsDecimal(10, 2).NotNullable().WithDefaultValue(0)
            .AddColumn("is_amount_manually_overridden").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("payment_method").AsString(20).Nullable()
            .AddColumn("is_paid").AsBoolean().NotNullable().WithDefaultValue(false);
    }
}

/// <summary>Kopira Appointment.amount/suggested_amount/is_amount_manually_overridden/payment_method/is_paid
/// na SVAKI Booking tog termina (vidi domensku napomenu na klasi iznad zašto se ne dijeli po broju klijenata).</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 14)]
public class CopyAppointmentCommercialFieldsToBookings : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            UPDATE dunelight.bookings b
            SET amount = a.amount,
                suggested_amount = a.suggested_amount,
                is_amount_manually_overridden = a.is_amount_manually_overridden,
                payment_method = a.payment_method,
                is_paid = a.is_paid
            FROM dunelight.appointments a
            WHERE b.appointment_id = a.id;");
    }
}

/// <summary>Uklanja komercijalna polja s Appointment tek NAKON što su prekopirana na Booking iznad —
/// razvojna faza dopušta breaking migraciju (vidi spec), podaci se svejedno čuvaju/prenose, ne bacaju.</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 15)]
public class DropCommercialColumnsFromAppointments : DuneLightMigration
{
    public override void Up()
    {
        Delete.Column("amount")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("suggested_amount")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("is_amount_manually_overridden")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("payment_method")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight);
        Delete.Column("is_paid")
            .FromTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight);
    }
}
