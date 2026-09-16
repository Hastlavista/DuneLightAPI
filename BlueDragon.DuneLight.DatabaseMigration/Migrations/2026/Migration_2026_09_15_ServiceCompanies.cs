using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Eksplicitna dostupnost usluge po poslovnici (Service ↔ Company many-to-many). Poslovno pravilo: bez
/// zapisa u ovoj tablici usluga je dostupna NIGDJE — nema implicitnog "prazno = svugdje" (vidi domensku
/// napomenu na ServiceCompany.cs). Nova usluga i nova poslovnica namjerno kreću bez ijedne dodjele; ova
/// migracija ne radi nikakav backfill postojećih Service/Company parova.
///
/// Namjerno BEZ organization_id (isti obrazac kao employee_companies/employee_services) — tenant
/// integritet provjerava ServiceAvailabilityService prije upisa (Service.OrganizationId ==
/// Company.OrganizationId == CurrentOrganizationId), a upiti u ServiceCompanyHandler dodatno filtriraju
/// preko join-a na Service/Company.organization_id.
///
/// ON DELETE CASCADE s obje strane: ovo je konfiguracijski, ne povijesni zapis, pa ne smije blokirati
/// hard-delete inače neiskorištenog Service/Company (vidi ServiceHandler/CompanyHandler.IsReferenced,
/// koji namjerno ne provjerava ovu tablicu).
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 3)]
public class CreateServiceCompaniesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ServiceCompanies)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_service_companies")
            .WithColumn("service_id").AsGuid().NotNullable()
            .WithColumn("company_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_service_companies_service_id")
            .FromTable(Tables.ServiceCompanies).InSchema(Tables.Schemas.DuneLight).ForeignColumn("service_id")
            .ToTable(Tables.Services).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_service_companies_company_id")
            .FromTable(Tables.ServiceCompanies).InSchema(Tables.Schemas.DuneLight).ForeignColumn("company_id")
            .ToTable(Tables.Companies).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_service_companies_service_company")
            .OnTable(Tables.ServiceCompanies).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("service_id").Ascending()
            .OnColumn("company_id").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_service_companies_company_id")
            .OnTable(Tables.ServiceCompanies).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("company_id").Ascending();
    }
}
