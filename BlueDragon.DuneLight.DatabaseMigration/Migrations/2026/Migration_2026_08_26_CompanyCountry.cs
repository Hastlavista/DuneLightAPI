using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Company.Country (ISO 3166-1 alpha-2) — određuje koji katalog fiksnih praznika CompanyHolidayService.Generate
/// koristi (vidi DefaultCompanyHolidays). Sve postojeće poslovnice defaultaju na "HR" (jedina trenutno podržana
/// država u katalogu) dok se ne uvede odabir prilikom kreiranja/uređivanja poslovnice izvan HR.
/// </summary>
[DeveloperMigration(2026, 08, 26, Developer.SilvioHabazin, 1)]
public class AddCountryToCompanies : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Companies)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("country").AsString(2).NotNullable().WithDefaultValue("HR");
    }
}
