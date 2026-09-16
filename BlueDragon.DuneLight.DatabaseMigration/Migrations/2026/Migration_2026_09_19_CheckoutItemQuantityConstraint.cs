using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// DB-level defense-in-depth za CheckoutItem.Quantity semantiku (application-level provjera ostaje primarna,
/// vidi CheckoutService.AddBookingItem/AddPackageItem/AddProductItem) — Booking/Package stavke su UVIJEK
/// Quantity=1 (nikad postavljene iz klijentskog unosa), Product stavka smije biti Quantity &gt;= 1 (vidi Products
/// &amp; Stock spec section 21, review fix section 3).
///
/// Sigurno za postojeće podatke: sve povijesne Booking stavke (backfill u
/// Migration_2026_09_17_CheckoutFoundation.MigrateBookingPaymentsToCheckoutItems) i sve Package stavke
/// (CheckoutService.AddPackageItem) su uvijek umetnute s quantity=1 — nema retka koji bi ovaj CHECK prekršio.
/// </summary>
[DeveloperMigration(2026, 09, 19, Developer.SilvioHabazin, 0)]
public class AddCheckoutItemQuantityCheckConstraint : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.checkout_items
            ADD CONSTRAINT ck_checkout_items_quantity CHECK (
                (type = 'Booking' AND quantity = 1) OR
                (type = 'Package' AND quantity = 1) OR
                (type = 'Product' AND quantity >= 1)
            );");
    }
}
