using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi Products &amp; Stock temelje (vidi Product.cs/ProductStock.cs/StockMovement.cs, Infrastructure, za punu
/// domensku napomenu) i proširuje Checkout/CheckoutItem s Product kao trećim tipiziranim subjektom (uz
/// postojeći Booking/Package, vidi Migration_2026_09_17_CheckoutFoundation).
/// </summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 0)]
public class CreateProductsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Products)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_products")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("description").AsCustom("text").Nullable()
            .WithColumn("sku").AsString(100).Nullable()
            .WithColumn("default_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_products_organization_id")
            .FromTable(Tables.Products).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_products_organization_id")
            .OnTable(Tables.Products).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending();
    }
}

/// <summary>Aktivni normalizirani (trim + case-insensitive) unique naziv po Organization — isti obrazac kao
/// ux_services_org_name_active/ux_companies_org_name_active (vidi ProductHandler.NameExistsAmongActive). Nova
/// tablica, nema postojećih podataka — bez OPREZ napomene koja prati te starije migracije.</summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 1)]
public class AddProductActiveNameUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_products_org_name_active " +
            "ON dunelight.products (organization_id, lower(trim(name))) WHERE is_active = true;");
    }
}

/// <summary>Opcionalan SKU, kad popunjen mora biti jedinstven po Organization (normalizirano trim/case-insensitive,
/// vidi spec section 5) — neovisno o IsActive (za razliku od naziva). Prazan string se tretira kao "nema SKU"
/// (isključen iz uniqueness, vidi ProductHandler.SkuExists).</summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 2)]
public class AddProductSkuUniqueIndex : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_products_org_sku " +
            "ON dunelight.products (organization_id, lower(trim(sku))) WHERE sku IS NOT NULL AND trim(sku) <> '';");
    }
}

/// <summary>
/// Materijalizirano trenutno stanje zalihe za par Product+Company — točno jedan redak po paru (vidi
/// ux_product_stock_product_company, ujedno ON CONFLICT cilj u ProductStockHandler.GetOrCreateForUpdate).
/// quantity &gt;= 0 kao CHECK constraint (spec section 11) — application-level provjera u StockService je
/// primarna obrana, ovo je posljednja linija obrane na DB razini.
/// </summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 3)]
public class CreateProductStockTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ProductStock)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_product_stock")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("product_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("quantity").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("updated_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_product_stock_product_id")
            .FromTable(Tables.ProductStock).InSchema(Tables.Schemas.DuneLight).ForeignColumn("product_id")
            .ToTable(Tables.Products).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_product_stock_company_id")
            .FromTable(Tables.ProductStock).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_product_stock_product_company")
            .OnTable(Tables.ProductStock).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("product_id").Ascending()
            .OnColumn("company_id").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_product_stock_org_company")
            .OnTable(Tables.ProductStock).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending();

        Execute.Sql("ALTER TABLE dunelight.product_stock ADD CONSTRAINT ck_product_stock_quantity_non_negative CHECK (quantity >= 0);");
    }
}

/// <summary>
/// Nepromjenjiva povijest kretanja zalihe — jedini izvor istine, ProductStock.quantity je materijalizirani
/// zbroj (vidi StockMovement.cs klasnu napomenu). checkout_item_id/related_company_id/transfer_correlation_id
/// su nullable jer se popunjavaju samo za odgovarajući Type (Sale odnosno TransferOut/TransferIn).
/// </summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 4)]
public class CreateStockMovementsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.StockMovements)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_stock_movements")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("product_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("type").AsString(20).NotNullable()
            .WithColumn("quantity_delta").AsInt32().NotNullable()
            .WithColumn("reason").AsCustom("text").Nullable()
            .WithColumn("checkout_item_id").AsGuid().Nullable()
            .WithColumn("related_company_id").AsGuid().Nullable()
            .WithColumn("transfer_correlation_id").AsGuid().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.ForeignKey("fk_stock_movements_product_id")
            .FromTable(Tables.StockMovements).InSchema(Tables.Schemas.DuneLight).ForeignColumn("product_id")
            .ToTable(Tables.Products).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_stock_movements_company_id")
            .FromTable(Tables.StockMovements).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_stock_movements_related_company_id")
            .FromTable(Tables.StockMovements).InSchema(Tables.Schemas.DuneLight).ForeignColumn("related_company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_stock_movements_checkout_item_id")
            .FromTable(Tables.StockMovements).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_item_id")
            .ToTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_stock_movements_org_product_created")
            .OnTable(Tables.StockMovements).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("product_id").Ascending()
            .OnColumn("created_at").Ascending();

        // Sprječava dvostruki Sale decrement iste CheckoutItem stavke (npr. na Checkout.Complete retry) — vidi
        // spec section 56/IStockLedgerService.ConsumeForSale.
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_stock_movements_sale_checkout_item " +
            "ON dunelight.stock_movements (checkout_item_id) WHERE type = 'Sale';");
    }
}

/// <summary>
/// Proširuje checkout_items s Product kao trećim tipiziranim subjektom (uz postojeći Booking/Package) —
/// zamjenjuje ck_checkout_items_subject CHECK constraint da uključi Product granu (vidi
/// Migration_2026_09_17_CheckoutFoundation.CreateCheckoutItemsTable za izvornu verziju). Nema backfilla —
/// postojeći Booking/Package retci ostaju netaknuti (product_id je NULL za njih, što zadovoljava novi CHECK).
/// </summary>
[DeveloperMigration(2026, 09, 18, Developer.SilvioHabazin, 5)]
public class AddProductIdToCheckoutItems : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.CheckoutItems)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("product_id").AsGuid().Nullable();

        Create.ForeignKey("fk_checkout_items_product_id")
            .FromTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("product_id")
            .ToTable(Tables.Products).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql("ALTER TABLE dunelight.checkout_items DROP CONSTRAINT ck_checkout_items_subject;");

        Execute.Sql(@"
            ALTER TABLE dunelight.checkout_items
            ADD CONSTRAINT ck_checkout_items_subject CHECK (
                (type = 'Booking' AND booking_id IS NOT NULL AND package_id IS NULL AND product_id IS NULL) OR
                (type = 'Package' AND package_id IS NOT NULL AND booking_id IS NULL AND product_id IS NULL) OR
                (type = 'Product' AND product_id IS NOT NULL AND booking_id IS NULL AND package_id IS NULL)
            );");
    }
}
