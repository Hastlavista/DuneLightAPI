using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Platform identity correction — platform_operators (user_id -> users.id) modeled platform access as a flag
/// on top of a tenant User, which is the wrong domain shape: a DuneLight platform operator is not a customer
/// and has no Organization. Retiring it in favor of platform_accounts (see CreatePlatformAccountsTable below),
/// a fully standalone identity with no FK to any tenant table. Mirrors DropServiceCategoriesTable's clean-drop
/// pattern (Migration_2026_08_21_RemoveServiceCategory.cs): FKs first, then the table.
/// </summary>
[DeveloperMigration(2026, 10, 03, Developer.SilvioHabazin, 0)]
public class DropPlatformOperatorsTable : DuneLightMigration
{
    public override void Up()
    {
        Delete.ForeignKey("fk_platform_operators_user_id")
            .OnTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight);

        Delete.ForeignKey("fk_platform_operators_granted_by")
            .OnTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight);

        Delete.ForeignKey("fk_platform_operators_revoked_by")
            .OnTable(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight);

        Delete.Table(Tables.PlatformOperators).InSchema(Tables.Schemas.DuneLight);
    }
}

/// <summary>
/// PlatformAccount — see the class's own doc comment (Infrastructure/Domain/Models/Management/PlatformAccount.cs)
/// for the full identity-separation rationale. No foreign keys at all: structurally independent of Organization
/// and User. Email is a STANDALONE unique constraint (unlike tenant users.email, which is only unique per
/// Organization — see Migration_2026_07.cs's uq_users_organization_id_email) because PlatformAccount has no
/// tenant scope to qualify it by.
/// </summary>
[DeveloperMigration(2026, 10, 03, Developer.SilvioHabazin, 1)]
public class CreatePlatformAccountsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.PlatformAccounts)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_platform_accounts")
            .WithColumn("email").AsString(255).NotNullable().Unique("uq_platform_accounts_email")
            .WithColumn("password_hash").AsString(255).NotNullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();
    }
}
