using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// price_list_item_history je do sada bilježio samo promjenu cijene (old_price/new_price). PriceListItemUpdateRequest
/// dopušta uređivanje i ValidFrom/ValidTo (uz Price — vidi PricingService.Update), pa audit mora hvatati i te promjene,
/// ne samo cijenu. Backfill nije potreban: postojeći redovi dobivaju old_valid_from/new_valid_from iz trenutnog stanja
/// price_list_items (najbolja dostupna aproksimacija za povijesne zapise gdje točan raspon u trenutku promjene nije
/// poznat), a old_valid_to/new_valid_to ostaju NULL dok se ne pojavi stvarna promjena datuma.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 4)]
public class AddPriceListItemHistoryDateRangeColumns : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.PriceListItemHistory).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("old_valid_from").AsDateTimeOffset().Nullable()
            .AddColumn("new_valid_from").AsDateTimeOffset().Nullable()
            .AddColumn("old_valid_to").AsDateTimeOffset().Nullable()
            .AddColumn("new_valid_to").AsDateTimeOffset().Nullable();

        Execute.Sql(
            "UPDATE dunelight.price_list_item_history h " +
            "SET old_valid_from = p.valid_from, new_valid_from = p.valid_from, " +
            "old_valid_to = p.valid_to, new_valid_to = p.valid_to " +
            "FROM dunelight.price_list_items p " +
            "WHERE p.id = h.price_list_item_id;");

        Execute.Sql(
            "ALTER TABLE dunelight.price_list_item_history " +
            "ALTER COLUMN old_valid_from SET NOT NULL, " +
            "ALTER COLUMN new_valid_from SET NOT NULL;");
    }
}
