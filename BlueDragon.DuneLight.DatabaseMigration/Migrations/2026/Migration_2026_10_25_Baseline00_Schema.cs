using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 0/11: shema `dunelight`. Zamjenjuje svu dosadašnju povijest
/// migracija; očekuje praznu (ispuštenu) shemu. Ne seeda ništa — prazna baza nema nijedan redak, podatke stvaraju
/// isključivo aplikacijski tokovi (npr. registracija organizacije, ADR-0023).
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 0)]
public class Baseline00Schema : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS dunelight;");
    }
}
