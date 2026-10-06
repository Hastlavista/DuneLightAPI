using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// ADR-0020 — email je jedinstven unutar organizacije bez obzira na velika/mala slova:
/// - klijenti: novi parcijalni unique indeks (organization_id, lower(email)) za ne-NULL email (klijent bez emaila je
///   dopušten; anonimizacija postavlja email na NULL i time ga oslobađa);
/// - korisnički računi: dosadašnji case-sensitive uq_users_organization_id_email zamijenjen funkcijskim
///   (organization_id, lower(email)).
/// Trim radi aplikacija (EmailNormalizer) pri svakom upisu. Razvojna baza (ADR-0003): bez backfilla/normalizacije
/// postojećih redaka i bez Down migracije — ako lokalni podaci već sadrže case-duplikate, baza se izgrađuje ponovno.
/// </summary>
[DeveloperMigration(2026, 10, 24, Developer.SilvioHabazin, 0)]
public class UniqueEmailCaseInsensitive : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            CREATE UNIQUE INDEX ux_clients_organization_email
                ON dunelight.{Tables.Clients} (organization_id, lower(email))
                WHERE email IS NOT NULL;

            DROP INDEX dunelight.uq_users_organization_id_email;
            CREATE UNIQUE INDEX ux_users_organization_email
                ON dunelight.{Tables.Users} (organization_id, lower(email));");
    }
}
