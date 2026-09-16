using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Performance-only: podržava OperationalDashboardService.Financial.TodayRevenue upit
/// (ICheckoutHandler.GetCompletedPaymentAmountForCompanyOnDate), koji filtrira Payment po
/// (organization_id, status = 'Completed', created_at raspon) pa spaja na Checkout radi company_id. Postojeći
/// ix_payments_org_checkout (Migration_2026_09_17_CheckoutFoundation) ne pokriva ovaj predikat — ne postoji
/// created_at/status u tom indeksu.
///
/// Djelomičan indeks (WHERE status = 'Completed') namjerno umjesto punog (organization_id, status, created_at)
/// — upit UVIJEK filtrira isključivo Completed (Voided se nikad ne broji u revenue, vidi
/// CheckoutFinancialsCalculator/BookingFinancialsCalculator napomenu), pa djelomičan indeks pokriva točno taj
/// predikat uz manji otisak od punog indeksa koji bi uključivao i Voided retke (isti obrazac djelomičnog
/// indeksa kao ux_checkout_items_locks_booking, vidi Migration_2026_09_17_CheckoutFoundation). Ne dira
/// Payment.CompanyId (ne postoji, namjerno se ne uvodi ovdje — Company se i dalje čita preko Checkout join-a,
/// vidi finding #9) niti mijenja semantiku prihoda/statusa.
/// </summary>
[DeveloperMigration(2026, 09, 20, Developer.SilvioHabazin, 0)]
public class AddPaymentDashboardIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            CREATE INDEX ix_payments_org_completed_created_at
            ON dunelight.payments (organization_id, created_at)
            WHERE status = 'Completed';");
    }
}
