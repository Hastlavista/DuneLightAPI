using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Uvodi Employee Commissions temelje (vidi CommissionRule.cs/CommissionEntry.cs, Infrastructure, za punu
/// domensku napomenu) — internu evidenciju zarađene provizije osoblja, odvojenu od Payment/Checkout financijskog
/// ledgera. Commission tracking kreće od ove migracije nadalje — nema backfilla za povijesne Bookinge/prodaje
/// (vidi spec section 56, namjerno: retroaktivna primjena današnjeg pravila na staru povijest bi bila nagađanje).
///
/// Datirano 2026-09-21 (NE 2026-09-18 kao u prvobitnoj verziji) — commission_rules.product_id FK zahtijeva da
/// products tablica već postoji (Migration_2026_09_18_ProductsAndStock), a prvobitna verzija je slučajno dijelila
/// isti datum I iste redoslijedne brojeve (0/1) s tom migracijom, što bi FluentMigrator odbio kao duplicirane
/// verzije. Pomaknuto iza cijelog 09-18/09-19/09-20 lanca (Products/Stock, CheckoutItem quantity constraint,
/// Payment dashboard index) da poredak bude deterministički. Ova migracija još nije bila primijenjena ni na
/// jednoj bazi (nova, neobjavljena značajka), pa je preimenovanje sigurno — ne dira nijednu već postojeću
/// migraciju.
/// </summary>
[DeveloperMigration(2026, 09, 21, Developer.SilvioHabazin, 0)]
public class CreateCommissionRulesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CommissionRules)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_commission_rules")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("subject_type").AsString(20).NotNullable()
            .WithColumn("service_id").AsGuid().Nullable()
            .WithColumn("product_id").AsGuid().Nullable()
            .WithColumn("package_id").AsGuid().Nullable()
            .WithColumn("calculation_type").AsString(20).NotNullable()
            .WithColumn("value").AsDecimal(10, 2).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_commission_rules_organization_id")
            .FromTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_rules_employee_id")
            .FromTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_rules_service_id")
            .FromTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_rules_product_id")
            .FromTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).ForeignColumn("product_id")
            .ToTable(Tables.Products).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_rules_package_id")
            .FromTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).ForeignColumn("package_id")
            .ToTable(Tables.Packages).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_commission_rules_org_employee")
            .OnTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending();

        // Vlasnik predmeta je točno jedno od service_id/product_id/package_id prema subject_type — isti
        // obrazac kao ck_checkout_items_subject.
        Execute.Sql(@"
            ALTER TABLE dunelight.commission_rules
            ADD CONSTRAINT ck_commission_rules_subject CHECK (
                (subject_type = 'Service' AND service_id IS NOT NULL AND product_id IS NULL AND package_id IS NULL) OR
                (subject_type = 'Product' AND product_id IS NOT NULL AND service_id IS NULL AND package_id IS NULL) OR
                (subject_type = 'Package' AND package_id IS NOT NULL AND service_id IS NULL AND product_id IS NULL)
            );");

        // Value >= 0 uvijek; Percentage dodatno <= 100 — backstop uz aplikacijsku validaciju
        // (CommissionRuleService), vidi spec section 7/49.
        Execute.Sql(@"
            ALTER TABLE dunelight.commission_rules
            ADD CONSTRAINT ck_commission_rules_value CHECK (
                value >= 0 AND (calculation_type = 'Fixed' OR value <= 100)
            );");

        // Najviše JEDNO aktivno pravilo po Employee+Subject — COALESCE pretvara NULL u sentinel jer standardni
        // Postgres unique indeks tretira NULL kao "nikad jednak" (dva retka s istim ServiceId a oba NULL
        // ProductId/PackageId inače NE bi kolidirala, vidi spec section 12, isti problem kao ostale "vlasnik je
        // točno jedno od X/Y" situacije da nemaju datumski raspon za razlikovanje).
        Execute.Sql(@"
            CREATE UNIQUE INDEX ux_commission_rules_employee_subject
            ON dunelight.commission_rules (
                organization_id, employee_id, subject_type,
                COALESCE(service_id, '00000000-0000-0000-0000-000000000000'),
                COALESCE(product_id, '00000000-0000-0000-0000-000000000000'),
                COALESCE(package_id, '00000000-0000-0000-0000-000000000000')
            )
            WHERE is_active = true;");
    }
}

/// <summary>
/// Nepromjenjiv staff-compensation ledger — vidi CommissionEntry.cs za punu domensku napomenu (posebice zašto
/// GroupService izvor referencira AppointmentId a NE BookingId, i zašto Status=Reversed trenutno nema
/// pozivatelja).
/// </summary>
[DeveloperMigration(2026, 09, 21, Developer.SilvioHabazin, 1)]
public class CreateCommissionEntriesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.CommissionEntries)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_commission_entries")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("employee_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable()
            .WithColumn("commission_rule_id").AsGuid().NotNullable()
            .WithColumn("source_type").AsString(20).NotNullable()
            .WithColumn("appointment_id").AsGuid().Nullable()
            .WithColumn("booking_id").AsGuid().Nullable()
            .WithColumn("checkout_item_id").AsGuid().Nullable()
            .WithColumn("base_amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("calculation_type").AsString(20).NotNullable()
            .WithColumn("rule_value").AsDecimal(10, 2).NotNullable()
            .WithColumn("commission_amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("earned_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_commission_entries_organization_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_entries_employee_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("employee_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_entries_company_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_commission_entries_commission_rule_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("commission_rule_id")
            .ToTable(Tables.CommissionRules).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Cascade s Appointment/Booking — isto ponašanje kao AppointmentAuditLog: "isti dan" tvrdo brisanje
        // termina (AppointmentService.Delete) briše i njegov commission trag, ne ostavlja osirotjeli redak s
        // razbijenom FK referencom (vidi CommissionEntry.cs).
        Create.ForeignKey("fk_commission_entries_appointment_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_commission_entries_booking_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_commission_entries_checkout_item_id")
            .FromTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("checkout_item_id")
            .ToTable(Tables.CheckoutItems).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ix_commission_entries_org_employee_earned")
            .OnTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("employee_id").Ascending()
            .OnColumn("earned_at").Ascending();

        Create.Index("ix_commission_entries_org_company_earned")
            .OnTable(Tables.CommissionEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("company_id").Ascending()
            .OnColumn("earned_at").Ascending();

        // Subjekt izvora je točno jedno od appointment_id (GroupService, booking_id prazan)/booking_id
        // (IndividualService, appointment_id popunjen)/checkout_item_id (Product/PackageSale) — vidi
        // CommissionEntry.cs.
        Execute.Sql(@"
            ALTER TABLE dunelight.commission_entries
            ADD CONSTRAINT ck_commission_entries_source CHECK (
                (source_type = 'IndividualService' AND booking_id IS NOT NULL AND appointment_id IS NOT NULL AND checkout_item_id IS NULL) OR
                (source_type = 'GroupService' AND appointment_id IS NOT NULL AND booking_id IS NULL AND checkout_item_id IS NULL) OR
                (source_type = 'ProductSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL) OR
                (source_type = 'PackageSale' AND checkout_item_id IS NOT NULL AND appointment_id IS NULL AND booking_id IS NULL)
            );");

        Execute.Sql("ALTER TABLE dunelight.commission_entries ADD CONSTRAINT ck_commission_entries_amount CHECK (commission_amount >= 0);");

        // Idempotencija — najviše jedan CommissionEntry po poslovnoj pojavi (vidi spec section 26, CommissionEntry.cs
        // klasnu napomenu). BookingId/CheckoutItemId su standardni (ne-djelomični) unique indeksi jer je Postgres
        // NULL-per-se-distinct ponašanje ovdje TOČNO ono što treba (GroupService/Product/Package retci imaju
        // booking_id=NULL i ne međusobno koliziraju; Individual/Group retci imaju checkout_item_id=NULL isto).
        Execute.Sql("CREATE UNIQUE INDEX ux_commission_entries_booking_id ON dunelight.commission_entries (booking_id) WHERE booking_id IS NOT NULL;");
        Execute.Sql("CREATE UNIQUE INDEX ux_commission_entries_checkout_item_id ON dunelight.commission_entries (checkout_item_id) WHERE checkout_item_id IS NOT NULL;");
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_commission_entries_group_appointment ON dunelight.commission_entries (appointment_id) " +
            "WHERE source_type = 'GroupService';");
    }
}
