using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Preimenuje Booking.package_entry_deducted/package_entry_returned(_at/_by) u package_coverage_applied/
/// package_coverage_returned(_at/_by) — staro ime je sugeriralo da polje uvijek znači numeričko smanjenje
/// brojača ulazaka, što nije bila dosljedna semantika između Individual (uvijek true kod odabranog paketa)
/// i Group (namjerno false za neograničene/MonthlyPackage pakete, gdje brojača nema) putanje. Novo ime i
/// značenje: "je li ClientPackage entitlement STVARNO PRIMIJENJEN na ovaj Booking" — postavlja se na true u
/// OBA slučaja (ograničen/neograničen paket) čim se check-in/completion stvarno razriješi paketom, neovisno
/// je li numerički brojač smanjen (vidi Booking.cs domensku napomenu, BookingFinancialsCalculator.IsPackageSettled).
/// Podaci se ne gube — samo prijenos vrijednosti pod novim imenom stupca (razvojna faza, vidi spec).
/// </summary>
[DeveloperMigration(2026, 09, 16, Developer.SilvioHabazin, 3)]
public class RenamePackageCoverageFlagsOnBookings : DuneLightMigration
{
    public override void Up()
    {
        Rename.Column("package_entry_deducted")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).To("package_coverage_applied");
        Rename.Column("package_entry_returned")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).To("package_coverage_returned");
        Rename.Column("package_entry_returned_at")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).To("package_coverage_returned_at");
        Rename.Column("package_entry_returned_by")
            .OnTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).To("package_coverage_returned_by");
    }
}

/// <summary>
/// Popravlja postojeće Group MonthlyPackage (neograničen paket) Booking retke koji su, prije ove izmjene,
/// imali package_coverage_applied=false SAMO zato što nema brojača za smanjiti (vidi domensku napomenu
/// gore) — takvi retci moraju sada ispravno pokazivati true (entitlement JEST bio primijenjen), inače bi
/// novi centralizirani BookingFinancialsCalculator.IsPackageSettled netočno prikazao postojeće, već odrađene
/// MonthlyPackage bookinge kao financijski nepodmirene. Cilja SAMO retke gdje je ClientPackageId popunjen,
/// coverage_type = 'MonthlyPackage' i coverage nikad nije vraćen — SessionPackage/SharedPool retci već imaju
/// ispravnu vrijednost (true kad je stvarno odbijeno) i ostaju netaknuti.
/// </summary>
[DeveloperMigration(2026, 09, 16, Developer.SilvioHabazin, 4)]
public class BackfillMonthlyPackageCoverageApplied : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            UPDATE dunelight.bookings
            SET package_coverage_applied = true
            WHERE client_package_id IS NOT NULL
              AND coverage_type = 'MonthlyPackage'
              AND package_coverage_applied = false
              AND package_coverage_returned = false;");
    }
}
