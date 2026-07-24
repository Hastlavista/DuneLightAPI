using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 0)]
public class CreateDuneLightSchema : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("CREATE SCHEMA IF NOT EXISTS dunelight;");
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 1)]
public class CreateOrganizationsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Organizations)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_organizations")
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("slug").AsString(255).NotNullable().Unique("uq_organizations_slug")
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();
    }
}

[DeveloperMigration(2026, 07, 20, Developer.SilvioHabazin, 2)]
public class CreateUsersTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Users)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_users")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("email").AsString(255).NotNullable()
            .WithColumn("password_hash").AsString(255).NotNullable()
            .WithColumn("api_key").AsString(255).NotNullable().Unique("uq_users_api_key")
            .WithColumn("role").AsString(20).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable();

        Create.ForeignKey("fk_users_organization_id")
            .FromTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("uq_users_organization_id_email")
            .OnTable(Tables.Users).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("email").Ascending()
            .WithOptions().Unique();
    }
}
