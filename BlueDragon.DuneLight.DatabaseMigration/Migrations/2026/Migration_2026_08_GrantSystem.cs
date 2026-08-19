using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 1)]
public class CreateGrantGroupsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.GrantGroups)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_grant_groups")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().Nullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_grant_groups_organization_id")
            .FromTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_grant_groups_organization_name")
            .OnTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("name").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 2)]
public class CreateGrantGroupGrantsTable : DuneLightMigration
{
    public override void Up()
    {
        // grant_key nema FK — katalog grantova živi u kodu (Grants.cs), ne u bazi (vidi FAZA 1 arhitektura).
        Create.Table(Tables.GrantGroupGrants)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_grant_group_grants")
            .WithColumn("grant_group_id").AsGuid().NotNullable()
            .WithColumn("grant_key").AsString(100).NotNullable();

        Create.ForeignKey("fk_grant_group_grants_grant_group_id")
            .FromTable(Tables.GrantGroupGrants).InSchema(Tables.Schemas.DuneLight).ForeignColumn("grant_group_id")
            .ToTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_grant_group_grants_group_key")
            .OnTable(Tables.GrantGroupGrants).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("grant_group_id").Ascending()
            .OnColumn("grant_key").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 3)]
public class CreateUserGrantGroupsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.UserGrantGroups)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_user_grant_groups")
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("grant_group_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_user_grant_groups_user_id")
            .FromTable(Tables.UserGrantGroups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("user_id")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_user_grant_groups_grant_group_id")
            .FromTable(Tables.UserGrantGroups).InSchema(Tables.Schemas.DuneLight).ForeignColumn("grant_group_id")
            .ToTable(Tables.GrantGroups).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_user_grant_groups_user_group")
            .OnTable(Tables.UserGrantGroups).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("user_id").Ascending()
            .OnColumn("grant_group_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 4)]
public class CreateRolesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Roles)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_roles")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().Nullable()
            .WithColumn("created_by").AsGuid().Nullable();

        Create.ForeignKey("fk_roles_organization_id")
            .FromTable(Tables.Roles).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_roles_organization_name")
            .OnTable(Tables.Roles).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("name").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 5)]
public class CreateUserRoleAssignmentsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.UserRoleAssignments)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_user_role_assignments")
            .WithColumn("user_id").AsGuid().NotNullable()
            .WithColumn("role_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_user_role_assignments_user_id")
            .FromTable(Tables.UserRoleAssignments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("user_id")
            .ToTable(Tables.Users).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_user_role_assignments_role_id")
            .FromTable(Tables.UserRoleAssignments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("role_id")
            .ToTable(Tables.Roles).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.Index("ux_user_role_assignments_user_role")
            .OnTable(Tables.UserRoleAssignments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("user_id").Ascending()
            .OnColumn("role_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 6)]
public class AddOwnerAndCredentialColumnsToUsers : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Users).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("is_owner").AsBoolean().NotNullable().WithDefaultValue(false);

        Alter.Table(Tables.Users).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("must_change_credentials_on_first_login").AsBoolean().NotNullable().WithDefaultValue(false);

        Alter.Table(Tables.Users).InSchema(Tables.Schemas.DuneLight)
            .AddColumn("pin_hash").AsString(255).Nullable();
    }
}

/// <summary>
/// Podatkovna migracija (FAZA 1 plan): svaka postojeća organizacija dobiva tri default GrantGroup-e
/// (Admin/Trener/Recepcija) s grantovima koji vjerno odgovaraju trenutnom Admin/Member/Reception ponašanju,
/// postojeći korisnici se dodjeljuju prema svojoj trenutnoj roli, a najstariji aktivni Admin po organizaciji
/// (po created_at, tj. onaj koji je odradio Register) postaje trajni Owner. Ništa ne smije ostati bez ijedne
/// dozvole — svaki Member/Reception korisnik dobiva točno jednu odgovarajuću grupu.
/// </summary>
[DeveloperMigration(2026, 08, 17, Developer.SilvioHabazin, 7)]
public class SeedDefaultGrantGroupsAndBackfillOwner : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            DO $$
            DECLARE
                org RECORD;
                admin_group_id uuid;
                trainer_group_id uuid;
                reception_group_id uuid;
                all_grants text[] := ARRAY[
                    'employees.directory.view','employees.view','employees.manage','employees.role.manage',
                    'employees.engagement-types.view','employees.engagement-types.manage',
                    'catalog.companies.view','catalog.companies.manage',
                    'catalog.service-categories.view','catalog.service-categories.manage',
                    'catalog.services.view','catalog.services.manage',
                    'catalog.packages.view','catalog.packages.manage',
                    'catalog.price-list.view','catalog.price-list.manage',
                    'clients.view','clients.manage','clients.status.manage','clients.anonymize',
                    'clients.tags.view','clients.tags.manage','clients.packages.view','clients.packages.manage',
                    'appointments.view','appointments.write.own','appointments.write.all','appointments.delete',
                    'groups.view','groups.manage','groups.attendance.view','groups.attendance.own','groups.attendance.all',
                    'roster.types.view','roster.types.manage','roster.entries.view','roster.entries.write.own','roster.entries.write.all',
                    'roster.reviews.team.view','roster.reviews.personal.view.own','roster.reviews.personal.view.all'
                ];
                trainer_grants text[] := ARRAY[
                    'employees.directory.view',
                    'catalog.companies.view','catalog.service-categories.view','catalog.services.view','catalog.packages.view','catalog.price-list.view',
                    'clients.view','clients.manage','clients.tags.view','clients.packages.view','clients.packages.manage',
                    'appointments.view','appointments.write.own',
                    'groups.view','groups.attendance.view','groups.attendance.own',
                    'roster.types.view','roster.entries.view','roster.entries.write.own',
                    'roster.reviews.team.view','roster.reviews.personal.view.own'
                ];
                reception_grants text[] := ARRAY[
                    'employees.directory.view','clients.view','clients.manage','clients.tags.view','clients.packages.view','clients.packages.manage'
                ];
                grant_key text;
                oldest_admin_user_id uuid;
            BEGIN
                FOR org IN SELECT id FROM dunelight.organizations LOOP
                    admin_group_id := (md5(random()::text || clock_timestamp()::text))::uuid;
                    trainer_group_id := (md5(random()::text || clock_timestamp()::text))::uuid;
                    reception_group_id := (md5(random()::text || clock_timestamp()::text))::uuid;

                    INSERT INTO dunelight.grant_groups (id, organization_id, name, created_at)
                    VALUES
                        (admin_group_id, org.id, 'Admin', now()),
                        (trainer_group_id, org.id, 'Trener', now()),
                        (reception_group_id, org.id, 'Recepcija', now());

                    FOREACH grant_key IN ARRAY all_grants LOOP
                        INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
                        VALUES ((md5(random()::text || clock_timestamp()::text))::uuid, admin_group_id, grant_key);
                    END LOOP;

                    FOREACH grant_key IN ARRAY trainer_grants LOOP
                        INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
                        VALUES ((md5(random()::text || clock_timestamp()::text))::uuid, trainer_group_id, grant_key);
                    END LOOP;

                    FOREACH grant_key IN ARRAY reception_grants LOOP
                        INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
                        VALUES ((md5(random()::text || clock_timestamp()::text))::uuid, reception_group_id, grant_key);
                    END LOOP;

                    INSERT INTO dunelight.user_grant_groups (id, user_id, grant_group_id)
                    SELECT (md5(random()::text || clock_timestamp()::text || u.id::text))::uuid, u.id, admin_group_id
                    FROM dunelight.users u WHERE u.organization_id = org.id AND u.role = 'Admin';

                    INSERT INTO dunelight.user_grant_groups (id, user_id, grant_group_id)
                    SELECT (md5(random()::text || clock_timestamp()::text || u.id::text))::uuid, u.id, trainer_group_id
                    FROM dunelight.users u WHERE u.organization_id = org.id AND u.role = 'Member';

                    INSERT INTO dunelight.user_grant_groups (id, user_id, grant_group_id)
                    SELECT (md5(random()::text || clock_timestamp()::text || u.id::text))::uuid, u.id, reception_group_id
                    FROM dunelight.users u WHERE u.organization_id = org.id AND u.role = 'Reception';

                    SELECT u.id INTO oldest_admin_user_id
                    FROM dunelight.users u
                    WHERE u.organization_id = org.id AND u.role = 'Admin'
                    ORDER BY u.created_at ASC NULLS LAST, u.id ASC
                    LIMIT 1;

                    IF oldest_admin_user_id IS NOT NULL THEN
                        UPDATE dunelight.users SET is_owner = true WHERE id = oldest_admin_user_id;
                    END IF;
                END LOOP;
            END $$;
        ");
    }
}