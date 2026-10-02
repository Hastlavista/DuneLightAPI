using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1F — višesegmentne grupe sa selektivnim sudjelovanjem:
/// 1. group_segment_templates — izvršna definicija grupe (usluga, pomak od sidra occurrencea, trajanje, prostorija, MEKI
///    poslovni kapacitet) + group_segment_template_resources (isti generički resursi kao segment). Usluga, prostorija i
///    kapacitet NISU više na grupi (groups.service_id/capacity/default_room_id se uklanjaju — jedini izvor je predložak);
///    trener (default_trainer_id) ostaje na grupi (pravilo osoblja: svaki generirani segment nasljeđuje istog trenera).
/// 2. group_member_segment_templates — eksplicitan odabir predložaka člana; group_id je dio oba kompozitna FK-a, pa
///    odabrani predložak i član MORAJU pripadati istoj grupi (bez okidača).
/// 3. appointment_segments.group_segment_template_id — koji predložak je generirao segment; jedinstven po terminu.
/// 4. waitlist_entries.appointment_segment_id — lista čekanja je po KONKRETNOM segmentu; jedinstvenost čekanja je
///    (segment, klijent).
///
/// Razvojna baza (sadržaj se ne čuva, bez složenog backfilla): zatečene grupe dobivaju JEDAN predložak iz dosadašnjih
/// polja (pomak 0, trajanje usluge), aktivni članovi ga biraju, generirani segmenti i stavke liste čekanja se vežu na
/// jedini segment/predložak svog occurrencea.
/// </summary>
[DeveloperMigration(2026, 10, 20, Developer.SilvioHabazin, 0)]
public class GroupSegmentTemplates : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($@"
            CREATE TABLE dunelight.group_segment_templates (
                id uuid NOT NULL,
                group_id uuid NOT NULL,
                service_id uuid NOT NULL,
                start_offset_minutes integer NOT NULL,
                duration_minutes integer NOT NULL,
                room_id uuid NULL,
                capacity integer NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NULL,
                CONSTRAINT pk_group_segment_templates PRIMARY KEY (id),
                CONSTRAINT ux_group_segment_templates_id_group UNIQUE (id, group_id),
                CONSTRAINT fk_group_segment_templates_group_id FOREIGN KEY (group_id) REFERENCES dunelight.{Tables.Groups} (id) ON DELETE CASCADE,
                CONSTRAINT fk_group_segment_templates_service_id FOREIGN KEY (service_id) REFERENCES dunelight.services (id) ON DELETE RESTRICT,
                CONSTRAINT fk_group_segment_templates_room_id FOREIGN KEY (room_id) REFERENCES dunelight.rooms (id) ON DELETE RESTRICT,
                CONSTRAINT ck_group_segment_templates_offset CHECK (start_offset_minutes >= 0 AND start_offset_minutes < 1440),
                CONSTRAINT ck_group_segment_templates_duration CHECK (duration_minutes > 0 AND duration_minutes <= 1440),
                CONSTRAINT ck_group_segment_templates_capacity CHECK (capacity >= 1));
            CREATE INDEX ix_group_segment_templates_group_id ON dunelight.group_segment_templates (group_id);

            CREATE TABLE dunelight.group_segment_template_resources (
                group_segment_template_id uuid NOT NULL,
                resource_id uuid NOT NULL,
                quantity_required integer NOT NULL,
                CONSTRAINT pk_group_segment_template_resources PRIMARY KEY (group_segment_template_id, resource_id),
                CONSTRAINT fk_group_segment_template_resources_template FOREIGN KEY (group_segment_template_id)
                    REFERENCES dunelight.group_segment_templates (id) ON DELETE CASCADE,
                CONSTRAINT fk_group_segment_template_resources_resource FOREIGN KEY (resource_id)
                    REFERENCES dunelight.resources (id) ON DELETE RESTRICT,
                CONSTRAINT ck_group_segment_template_resources_quantity CHECK (quantity_required > 0));

            ALTER TABLE dunelight.{Tables.GroupMembers} ADD CONSTRAINT ux_group_members_id_group UNIQUE (id, group_id);

            CREATE TABLE dunelight.group_member_segment_templates (
                group_member_id uuid NOT NULL,
                group_segment_template_id uuid NOT NULL,
                group_id uuid NOT NULL,
                CONSTRAINT pk_group_member_segment_templates PRIMARY KEY (group_member_id, group_segment_template_id),
                CONSTRAINT fk_group_member_segment_templates_member FOREIGN KEY (group_member_id, group_id)
                    REFERENCES dunelight.{Tables.GroupMembers} (id, group_id) ON DELETE CASCADE,
                CONSTRAINT fk_group_member_segment_templates_template FOREIGN KEY (group_segment_template_id, group_id)
                    REFERENCES dunelight.group_segment_templates (id, group_id) ON DELETE RESTRICT);
            CREATE INDEX ix_group_member_segment_templates_template ON dunelight.group_member_segment_templates (group_segment_template_id);

            ALTER TABLE dunelight.{Tables.AppointmentSegments}
                ADD COLUMN group_segment_template_id uuid NULL,
                ADD CONSTRAINT fk_appointment_segments_group_segment_template_id FOREIGN KEY (group_segment_template_id)
                    REFERENCES dunelight.group_segment_templates (id) ON DELETE RESTRICT;
            CREATE UNIQUE INDEX ux_appointment_segments_appointment_template
                ON dunelight.{Tables.AppointmentSegments} (appointment_id, group_segment_template_id)
                WHERE group_segment_template_id IS NOT NULL;

            -- Backfill (razvojni podaci): jedan predložak po grupi iz dosadašnjih polja.
            INSERT INTO dunelight.group_segment_templates (id, group_id, service_id, start_offset_minutes, duration_minutes, room_id, capacity, created_at)
            SELECT md5('group-template:' || g.id::text)::uuid, g.id, g.service_id, 0, s.default_duration_minutes, g.default_room_id, g.capacity, now()
              FROM dunelight.{Tables.Groups} g JOIN dunelight.services s ON s.id = g.service_id;
            INSERT INTO dunelight.group_member_segment_templates (group_member_id, group_segment_template_id, group_id)
            SELECT m.id, md5('group-template:' || m.group_id::text)::uuid, m.group_id
              FROM dunelight.{Tables.GroupMembers} m;
            UPDATE dunelight.{Tables.AppointmentSegments} seg
               SET group_segment_template_id = md5('group-template:' || a.group_id::text)::uuid
              FROM dunelight.{Tables.Appointments} a
             WHERE a.id = seg.appointment_id AND a.group_id IS NOT NULL;

            ALTER TABLE dunelight.{Tables.Groups}
                DROP COLUMN service_id,
                DROP COLUMN capacity,
                DROP COLUMN default_room_id;

            ALTER TABLE dunelight.{Tables.WaitlistEntries} ADD COLUMN appointment_segment_id uuid NULL;
            UPDATE dunelight.{Tables.WaitlistEntries} w
               SET appointment_segment_id = (SELECT seg.id FROM dunelight.{Tables.AppointmentSegments} seg
                                              WHERE seg.appointment_id = w.appointment_id ORDER BY seg.planned_start, seg.id LIMIT 1);
            DELETE FROM dunelight.{Tables.WaitlistEntries} WHERE appointment_segment_id IS NULL;
            ALTER TABLE dunelight.{Tables.WaitlistEntries}
                ALTER COLUMN appointment_segment_id SET NOT NULL,
                ADD CONSTRAINT fk_waitlist_entries_appointment_segment_id FOREIGN KEY (appointment_segment_id)
                    REFERENCES dunelight.{Tables.AppointmentSegments} (id) ON DELETE CASCADE;
            DROP INDEX dunelight.ux_waitlist_entries_appointment_client_waiting;
            CREATE UNIQUE INDEX ux_waitlist_entries_segment_client_waiting
                ON dunelight.{Tables.WaitlistEntries} (appointment_segment_id, client_id) WHERE status = 'Waiting';
            CREATE INDEX ix_waitlist_entries_segment_status_joined
                ON dunelight.{Tables.WaitlistEntries} (appointment_segment_id, status, joined_at);");
    }

    public override void Down()
    {
        Execute.Sql($@"
            DROP INDEX dunelight.ix_waitlist_entries_segment_status_joined;
            DROP INDEX dunelight.ux_waitlist_entries_segment_client_waiting;
            CREATE UNIQUE INDEX ux_waitlist_entries_appointment_client_waiting
                ON dunelight.{Tables.WaitlistEntries} (appointment_id, client_id) WHERE status = 'Waiting';
            ALTER TABLE dunelight.{Tables.WaitlistEntries} DROP COLUMN appointment_segment_id;

            ALTER TABLE dunelight.{Tables.Groups}
                ADD COLUMN service_id uuid NULL,
                ADD COLUMN capacity integer NULL,
                ADD COLUMN default_room_id uuid NULL;
            UPDATE dunelight.{Tables.Groups} g
               SET service_id = t.service_id, capacity = t.capacity, default_room_id = t.room_id
              FROM (SELECT DISTINCT ON (group_id) * FROM dunelight.group_segment_templates ORDER BY group_id, start_offset_minutes, id) t
             WHERE t.group_id = g.id;

            DROP INDEX dunelight.ux_appointment_segments_appointment_template;
            ALTER TABLE dunelight.{Tables.AppointmentSegments} DROP COLUMN group_segment_template_id;
            DROP TABLE dunelight.group_member_segment_templates;
            ALTER TABLE dunelight.{Tables.GroupMembers} DROP CONSTRAINT ux_group_members_id_group;
            DROP TABLE dunelight.group_segment_template_resources;
            DROP TABLE dunelight.group_segment_templates;");
    }
}

/// <summary>
/// Phase M1F — meki (poslovni) kapacitet segmenta grupe smije se prekoračiti SAMO eksplicitnim zahtjevom uz raw grant
/// "groups.capacity.override". Ista capability mehanika kao ResourcesCapabilitySeedData: JEDNA nova capability (model None,
/// Sensitive) i NOVA "admin" DefaultRoleTemplate verzija (v5 = v4 + ova capability na On). Trener/Recepcija je ne dobivaju.
/// </summary>
public static class GroupCapacityOverrideCapabilitySeedData
{
    public const int CapabilityVersion = 1;
    public const int AdminTemplateVersion = 5;

    public const string CapabilityKey = "groups.capacity.override";

    public static Guid CapabilityId() => DeterministicGuid($"capability-definition-v{CapabilityVersion}:{CapabilityKey}");

    public static Guid AdminTemplateId() => DeterministicGuid($"default-role-template-v{AdminTemplateVersion}:admin");

    private static Guid DeterministicGuid(string seed)
    {
        using MD5 md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(seed)));
    }

    public static readonly (string GrantKey, string Role)[] Grants =
    {
        ("groups.capacity.override", "PrimaryNoScope"),
    };
}

[DeveloperMigration(2026, 10, 20, Developer.SilvioHabazin, 1)]
public class SeedGroupCapacityOverrideCapability : DuneLightMigration
{
    public override void Up()
    {
        Guid capabilityId = GroupCapacityOverrideCapabilitySeedData.CapabilityId();
        Insert.IntoTable(Tables.CapabilityDefinitions).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = capabilityId,
            key = GroupCapacityOverrideCapabilitySeedData.CapabilityKey,
            version = GroupCapacityOverrideCapabilitySeedData.CapabilityVersion,
            category_key = "groups",
            scope_model = "None",
            sensitivity = "Sensitive",
            is_active = true,
            created_at = DateTimeOffset.UtcNow
        });

        foreach ((string grantKey, string role) in GroupCapacityOverrideCapabilitySeedData.Grants)
            Insert.IntoTable(Tables.CapabilityDefinitionGrants).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                capability_definition_id = capabilityId,
                grant_key = grantKey,
                role
            });
    }
}

/// <summary>Admin v5 = Admin v4 (vidi SeedAdminTemplateV4) + groups.capacity.override = On + isti compatibility extras.</summary>
[DeveloperMigration(2026, 10, 20, Developer.SilvioHabazin, 2)]
public class SeedAdminTemplateV5 : DuneLightMigration
{
    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid templateId = GroupCapacityOverrideCapabilitySeedData.AdminTemplateId();
        CapabilityV1SeedData.TemplateSeed adminV2 = CapabilityV2SeedData.Templates.Single(t => t.Key == "admin");

        Insert.IntoTable(Tables.DefaultRoleTemplates).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = templateId,
            key = "admin",
            version = GroupCapacityOverrideCapabilitySeedData.AdminTemplateVersion,
            display_name_hr = adminV2.DisplayNameHr,
            is_active = true,
            created_at = now
        });

        foreach (CapabilityV1SeedData.TemplateCapabilitySeed selection in adminV2.Selections)
            InsertSelection(templateId, CapabilityV1SeedData.CapabilityId(selection.CapabilityKey), selection.SelectedScope);

        InsertSelection(templateId, PermissionAdministrationCapabilitySeedData.CapabilityId(), "Manage");
        InsertSelection(templateId, ResourcesCapabilitySeedData.CapabilityId(), "Manage");
        InsertSelection(templateId, GroupCapacityOverrideCapabilitySeedData.CapabilityId(), "On");

        foreach (string grantKey in adminV2.CompatibilityExtraGrants)
            Insert.IntoTable(Tables.DefaultRoleTemplateGrants).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id = Guid.NewGuid(),
                default_role_template_id = templateId,
                grant_key = grantKey,
                reason = "CompatibilityExtra"
            });
    }

    private void InsertSelection(Guid templateId, Guid capabilityDefinitionId, string selectedScope)
    {
        Insert.IntoTable(Tables.DefaultRoleTemplateCapabilities).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            default_role_template_id = templateId,
            capability_definition_id = capabilityDefinitionId,
            selected_scope = selectedScope
        });
    }
}
