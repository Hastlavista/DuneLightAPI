using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// "Location" je bio pogrešan naziv za koncept — riječ je o tvrtkama (companies) kod kojih treneri
/// obavljaju dužnost, ne o fizičkim lokacijama. Preimenuje već postojeće tablice/stupce/constraintove
/// na bazama gdje su prijašnje migracije već odrađene (vidi CreateLocationsTable i dr.).
/// </summary>
[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 0)]
public class RenameLocationToCompany : DuneLightMigration
{
    public override void Up()
    {
        Rename.Table("locations").InSchema(Tables.Schemas.DuneLight).To(Tables.Companies);
        Rename.Table("employee_locations").InSchema(Tables.Schemas.DuneLight).To(Tables.EmployeeCompanies);

        Rename.Column("location_id").OnTable(Tables.PriceListItems).InSchema(Tables.Schemas.DuneLight).To("company_id");
        Rename.Column("location_id").OnTable(Tables.Groups).InSchema(Tables.Schemas.DuneLight).To("company_id");
        Rename.Column("location_id").OnTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).To("company_id");
        Rename.Column("location_id").OnTable(Tables.EmployeeCompanies).InSchema(Tables.Schemas.DuneLight).To("company_id");
        Rename.Column("home_location_id").OnTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).To("home_company_id");

        Execute.Sql("ALTER TABLE dunelight.companies RENAME CONSTRAINT pk_locations TO pk_companies;");
        Execute.Sql("ALTER TABLE dunelight.companies RENAME CONSTRAINT fk_locations_organization_id TO fk_companies_organization_id;");
        Execute.Sql("ALTER INDEX dunelight.ix_locations_organization_id RENAME TO ix_companies_organization_id;");

        Execute.Sql("ALTER TABLE dunelight.employee_companies RENAME CONSTRAINT pk_employee_locations TO pk_employee_companies;");
        Execute.Sql("ALTER TABLE dunelight.employee_companies RENAME CONSTRAINT fk_employee_locations_employee_id TO fk_employee_companies_employee_id;");
        Execute.Sql("ALTER TABLE dunelight.employee_companies RENAME CONSTRAINT fk_employee_locations_location_id TO fk_employee_companies_company_id;");
        Execute.Sql("ALTER INDEX dunelight.ux_employee_locations_employee_location RENAME TO ux_employee_companies_employee_company;");
        Execute.Sql("ALTER INDEX dunelight.ux_employee_locations_primary RENAME TO ux_employee_companies_primary;");

        Execute.Sql("ALTER TABLE dunelight.price_list_items RENAME CONSTRAINT fk_price_list_items_location_id TO fk_price_list_items_company_id;");
        Execute.Sql("ALTER INDEX dunelight.ix_price_list_items_subject_location RENAME TO ix_price_list_items_subject_company;");

        Execute.Sql("ALTER TABLE dunelight.groups RENAME CONSTRAINT fk_groups_location_id TO fk_groups_company_id;");

        Execute.Sql("ALTER TABLE dunelight.appointments RENAME CONSTRAINT fk_appointments_location_id TO fk_appointments_company_id;");
        Execute.Sql("ALTER INDEX dunelight.ix_appointments_org_location_startsat RENAME TO ix_appointments_org_company_startsat;");

        Execute.Sql("ALTER TABLE dunelight.clients RENAME CONSTRAINT fk_clients_home_location_id TO fk_clients_home_company_id;");
    }
}