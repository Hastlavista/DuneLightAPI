using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// DuneLight Platform Management Phase 1 — platform_operators je eksplicitna, tenant-nezavisna dozvola pristupa
/// (vidi RequirePlatformAccessAttribute/PlatformAccessGuard): aktivan redak (revoked_at IS NULL) = pristup,
/// odsutnost retka ili revoked_at != null = bez pristupa. Bez default-a za postojeće korisnike — tablica kreće
/// prazna, pa niti jedan postojeći korisnik ne dobiva platform pristup ovom migracijom. Ne dira organizations,
/// users, grant_groups, ni bilo koju tenant/registracijsku tablicu. revoked_at/revoked_by (soft-revoke, ne
/// DELETE) postoji da PlatformOperatorBootstrapper zna je li korisnik VEĆ IKAD bio bootstrapan — vidi
/// PlatformOperator klasnu napomenu.
/// </summary>
[DeveloperMigration(2026, 10, 02, Developer.SilvioHabazin, 0)]
public class CreatePlatformOperatorsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PlatformOperators)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_platform_operators")
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("granted_at").AsDateTimeOffset().NotNullable()
            .WithColumn("granted_by").AsGuid().Nullable()
            .WithColumn("revoked_at").AsDateTimeOffset().Nullable()
            .WithColumn("revoked_by").AsGuid().Nullable();

        Create.ForeignKey("fk_platform_operators_user_id")
            .FromTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight).ForeignColumn("user_id")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_platform_operators_granted_by")
            .FromTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight).ForeignColumn("granted_by")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_platform_operators_revoked_by")
            .FromTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight).ForeignColumn("revoked_by")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Jedinstveno po korisniku (ne djelomično po aktivnima) — jedan povijesni platform_operators redak po
        // korisniku, isti se redak revocira/(hipotetski) ponovno aktivira umjesto umetanja novog, vidi
        // PlatformOperatorHandler.EnsureBootstrapGranted.
        Create.Index("ux_platform_operators_user_id")
            .OnTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("user_id").Ascending()
            .WithOptions().Unique();
    }
}
