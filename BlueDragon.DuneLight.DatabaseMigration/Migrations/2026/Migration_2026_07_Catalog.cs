using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 3)]
public class CreateLocationsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Locations)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_locations")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("address").AsString(500).Nullable()
            .WithColumn("phone").AsString(50).Nullable()
            .WithColumn("color_hex").AsString(7).Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_locations_organization_id")
            .FromTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_locations_organization_id")
            .OnTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 4)]
public class CreateServiceCategoriesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ServiceCategories)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_service_categories")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("execution_mode").AsString(20).NotNullable()
            .WithColumn("color_hex").AsString(7).Nullable()
            .WithColumn("requires_client").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("counts_toward_revenue").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("description").AsCustom("text").Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_service_categories_organization_id")
            .FromTable(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Djelomičan unique indeks (samo među aktivnima) — FluentMigrator nema portabilan "WHERE" na indeksu, pa ide raw SQL.
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_service_categories_org_name_active " +
            "ON dunelight.service_categories (organization_id, name) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 5)]
public class CreateServicesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Services)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_services")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("service_category_id").AsGuid().NotNullable()
            .WithColumn("default_duration_minutes").AsInt32().NotNullable()
            .WithColumn("default_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("description").AsCustom("text").Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_services_organization_id")
            .FromTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_services_service_category_id")
            .FromTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_category_id")
            .ToTable(Tables.ServiceCategories).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_services_org_name_active " +
            "ON dunelight.services (organization_id, name) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 6)]
public class CreatePackagesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Packages)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_packages")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("description").AsCustom("text").Nullable()
            .WithColumn("entry_mode").AsString(20).NotNullable()
            .WithColumn("total_entry_count").AsInt32().Nullable()
            .WithColumn("validity_type").AsString(20).NotNullable()
            .WithColumn("validity_days").AsInt32().Nullable()
            .WithColumn("validity_fixed_date").AsDateTimeOffset().Nullable()
            .WithColumn("default_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_packages_organization_id")
            .FromTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 7)]
public class CreatePackageServicesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PackageServices)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_package_services")
            .WithColumn("package_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("entry_count").AsInt32().Nullable();

        Create.ForeignKey("fk_package_services_package_id")
            .FromTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight).ForeignColumn("package_id")
            .ToTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_package_services_service_id")
            .FromTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_package_services_package_service")
            .OnTable(Tables.PackageServices).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("package_id").Ascending()
            .OnColumn("service_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 8)]
public class CreatePriceListItemsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PriceListItems)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_price_list_items")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("service_id").AsGuid().Nullable()
            .WithColumn("package_id").AsGuid().Nullable()
            .WithColumn("location_id").AsGuid().Nullable()
            .WithColumn("price").AsDecimal(10, 2).NotNullable()
            .WithColumn("valid_from").AsDateTimeOffset().NotNullable()
            .WithColumn("valid_to").AsDateTimeOffset().Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_price_list_items_organization_id")
            .FromTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_price_list_items_service_id")
            .FromTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_price_list_items_package_id")
            .FromTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("package_id")
            .ToTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_price_list_items_location_id")
            .FromTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).ForeignColumn("location_id")
            .ToTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_price_list_items_subject_location")
            .OnTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("service_id").Ascending()
            .OnColumn("package_id").Ascending()
            .OnColumn("location_id").Ascending();

        // Predmet cijene je točno jedno od service_id/package_id.
        Execute.Sql(
            "ALTER TABLE dunelight.price_list_items ADD CONSTRAINT ck_price_list_items_subject " +
            "CHECK ((service_id IS NOT NULL AND package_id IS NULL) OR (service_id IS NULL AND package_id IS NOT NULL));");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 9)]
public class CreatePriceListItemHistoryTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PriceListItemHistory)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_price_list_item_history")
            .WithColumn("price_list_item_id").AsGuid().NotNullable()
            .WithColumn("old_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("new_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("changed_at").AsDateTimeOffset().NotNullable()
            .WithColumn("changed_by").AsGuid().Nullable();

        // Bez ON DELETE CASCADE — ako stavka ima povijest, ne smije se trajno obrisati (vidi PricingService.Delete).
        Create.ForeignKey("fk_price_list_item_history_price_list_item_id")
            .FromTable(Tables.PriceListItemHistory).InSchema(Tables.Schemas.DuneLight).ForeignColumn("price_list_item_id")
            .ToTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_price_list_item_history_price_list_item_id")
            .OnTable(Tables.PriceListItemHistory).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("price_list_item_id").Ascending();
    }
}
