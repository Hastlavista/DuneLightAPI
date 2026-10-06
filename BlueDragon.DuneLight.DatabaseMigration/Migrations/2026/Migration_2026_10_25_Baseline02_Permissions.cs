using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Početna migracija (ADR-0022), korak 2/11: grant grupe, dodjele i workforce uloge (katalog grantova je u kodu, ADR-0023).
/// Gradi isključivo trenutnu ciljnu shemu; ne seeda podatke.
/// </summary>
[DeveloperMigration(2026, 10, 25, Developer.SilvioHabazin, 2)]
public class Baseline02Permissions : DuneLightMigration
{
    public override void Up()
    {
        // grant_groups
        Execute.Sql(@"
            CREATE TABLE dunelight.grant_groups (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                system_key character varying(50),
                created_at timestamp with time zone,
                created_by uuid,
                updated_at timestamp with time zone,
                updated_by uuid
            );

            ALTER TABLE dunelight.grant_groups ADD CONSTRAINT pk_grant_groups PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_grant_groups_organization_name ON dunelight.grant_groups USING btree (organization_id, name);

            CREATE UNIQUE INDEX ux_grant_groups_organization_system_key ON dunelight.grant_groups USING btree (organization_id, system_key) WHERE (system_key IS NOT NULL);

            ALTER TABLE dunelight.grant_groups ADD CONSTRAINT fk_grant_groups_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // grant_group_grants
        Execute.Sql(@"
            CREATE TABLE dunelight.grant_group_grants (
                id uuid NOT NULL,
                grant_group_id uuid NOT NULL,
                grant_key character varying(100) NOT NULL
            );

            ALTER TABLE dunelight.grant_group_grants ADD CONSTRAINT pk_grant_group_grants PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_grant_group_grants_group_key ON dunelight.grant_group_grants USING btree (grant_group_id, grant_key);

            ALTER TABLE dunelight.grant_group_grants ADD CONSTRAINT fk_grant_group_grants_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;");

        // user_grant_groups
        Execute.Sql(@"
            CREATE TABLE dunelight.user_grant_groups (
                id uuid NOT NULL,
                user_id uuid NOT NULL,
                grant_group_id uuid NOT NULL
            );

            ALTER TABLE dunelight.user_grant_groups ADD CONSTRAINT pk_user_grant_groups PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_user_grant_groups_user_group ON dunelight.user_grant_groups USING btree (user_id, grant_group_id);

            ALTER TABLE dunelight.user_grant_groups ADD CONSTRAINT fk_user_grant_groups_grant_group_id FOREIGN KEY (grant_group_id) REFERENCES dunelight.grant_groups(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.user_grant_groups ADD CONSTRAINT fk_user_grant_groups_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id) ON DELETE CASCADE;");

        // roles
        Execute.Sql(@"
            CREATE TABLE dunelight.roles (
                id uuid NOT NULL,
                organization_id uuid NOT NULL,
                name character varying(255) NOT NULL,
                created_at timestamp with time zone,
                created_by uuid
            );

            ALTER TABLE dunelight.roles ADD CONSTRAINT pk_roles PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_roles_organization_name ON dunelight.roles USING btree (organization_id, name);

            ALTER TABLE dunelight.roles ADD CONSTRAINT fk_roles_organization_id FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id);");

        // user_role_assignments
        Execute.Sql(@"
            CREATE TABLE dunelight.user_role_assignments (
                id uuid NOT NULL,
                user_id uuid NOT NULL,
                role_id uuid NOT NULL
            );

            ALTER TABLE dunelight.user_role_assignments ADD CONSTRAINT pk_user_role_assignments PRIMARY KEY (id);

            CREATE UNIQUE INDEX ux_user_role_assignments_user_role ON dunelight.user_role_assignments USING btree (user_id, role_id);

            ALTER TABLE dunelight.user_role_assignments ADD CONSTRAINT fk_user_role_assignments_role_id FOREIGN KEY (role_id) REFERENCES dunelight.roles(id) ON DELETE CASCADE;

            ALTER TABLE dunelight.user_role_assignments ADD CONSTRAINT fk_user_role_assignments_user_id FOREIGN KEY (user_id) REFERENCES dunelight.users(id) ON DELETE CASCADE;");
    }
}
