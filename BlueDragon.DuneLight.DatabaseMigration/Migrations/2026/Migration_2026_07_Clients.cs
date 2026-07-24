using System;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 17)]
public class CreateClientTagsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ClientTags)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_client_tags")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("color_hex").AsString(7).Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("sort_order").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_client_tags_organization_id")
            .FromTable(Tables.ClientTags).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Execute.Sql(
            "CREATE UNIQUE INDEX ux_client_tags_org_name_active " +
            "ON dunelight.client_tags (organization_id, name) WHERE is_active = true;");
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 18)]
public class CreateClientsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.Clients)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_clients")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("member_number").AsInt32().NotNullable()
            .WithColumn("first_name").AsString(255).NotNullable()
            .WithColumn("last_name").AsString(255).NotNullable()
            .WithColumn("date_of_birth").AsDateTimeOffset().Nullable()
            .WithColumn("occupation").AsString(255).Nullable()
            .WithColumn("phone").AsString(255).Nullable()
            .WithColumn("email").AsString(255).Nullable()
            .WithColumn("note").AsCustom("text").Nullable()
            .WithColumn("health_note").AsCustom("text").Nullable()
            .WithColumn("gdpr_consent_given").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("gdpr_consent_date").AsDateTimeOffset().Nullable()
            .WithColumn("home_location_id").AsGuid().Nullable()
            .WithColumn("home_trainer_id").AsGuid().Nullable()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("is_anonymized").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("anonymized_at").AsDateTimeOffset().Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_clients_organization_id")
            .FromTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_clients_home_location_id")
            .FromTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("home_location_id")
            .ToTable(Tables.Locations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_clients_home_trainer_id")
            .FromTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).ForeignColumn("home_trainer_id")
            .ToTable(Tables.Employees).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_clients_org_member_number")
            .OnTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("member_number").Ascending()
            .WithOptions().Unique();

        // Podržava brzu pretragu po prezimenu/imenu (baza raste na tisuće klijenata).
        Create.Index("ix_clients_org_last_first_name")
            .OnTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("last_name").Ascending()
            .OnColumn("first_name").Ascending();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 19)]
public class CreateClientTagAssignmentsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.ClientTagAssignments)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_client_tag_assignments")
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("tag_id").AsGuid().NotNullable();

        Create.ForeignKey("fk_client_tag_assignments_client_id")
            .FromTable(Tables.ClientTagAssignments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(System.Data.Rule.Cascade);

        Create.ForeignKey("fk_client_tag_assignments_tag_id")
            .FromTable(Tables.ClientTagAssignments).InSchema(Tables.Schemas.DuneLight).ForeignColumn("tag_id")
            .ToTable(Tables.ClientTags).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.Index("ux_client_tag_assignments_client_tag")
            .OnTable(Tables.ClientTagAssignments).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("client_id").Ascending()
            .OnColumn("tag_id").Ascending()
            .WithOptions().Unique();
    }
}

[DeveloperMigration(2026, 07, 21, Developer.SilvioHabazin, 20)]
public class SeedClientsMinimal : DuneLightMigration
{
    // Isti fiksni GUID kao BlueDragon.DuneLight.Core.Shared.DevDefaults.OrganizationId — ista demo
    // organizacija koju je posijala migracija Zaposlenika (SeedEmployeesMinimal).
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid TagVip = Guid.NewGuid();
    private static readonly Guid TagWorkshops = Guid.NewGuid();
    private static readonly Guid TagPhotoConsent = Guid.NewGuid();

    private static readonly Guid ClientIvana = Guid.NewGuid();
    private static readonly Guid ClientPetar = Guid.NewGuid();
    private static readonly Guid ClientAna = Guid.NewGuid();
    private static readonly Guid ClientMarko = Guid.NewGuid();

    public override void Up()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string org = OrganizationId.ToString();

        (Guid Id, string Name, string ColorHex, int Sort)[] tags =
        {
            (TagVip, "VIP", "#EE6C4D", 0),
            (TagWorkshops, "Radionice", "#3D5A80", 1),
            (TagPhotoConsent, "Foto/video pristanak", "#98C1D9", 2)
        };

        foreach ((Guid id, string name, string colorHex, int sort) in tags)
        {
            Insert.IntoTable(Tables.ClientTags).InSchema(Tables.Schemas.DuneLight).Row(new
            {
                id,
                organization_id = OrganizationId,
                name,
                color_hex = colorHex,
                is_active = true,
                sort_order = sort,
                created_at = now
            });
        }

        // Ivana: zdravstvena napomena + matična lokacija/trener (preuzeti iz seed-a Zaposlenika po nazivu/e-mailu).
        Execute.Sql($@"
            INSERT INTO dunelight.clients
                (id, organization_id, member_number, first_name, last_name, date_of_birth, occupation,
                 phone, email, note, health_note, gdpr_consent_given, gdpr_consent_date,
                 home_location_id, home_trainer_id, is_active, is_anonymized, created_at)
            VALUES
                ('{ClientIvana}', '{org}', 1, 'Ivana', 'Kovač', '1990-03-14T00:00:00+00:00', 'Učiteljica',
                 '091 111 1111', 'ivana.kovac@example.com', NULL, 'Bol u koljenu, izbjegavati duboke čučnjeve.',
                 true, '{now:yyyy-MM-ddTHH:mm:sszzz}',
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = 'Centar'),
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Marko' AND last_name = 'Trener'),
                 true, false, '{now:yyyy-MM-ddTHH:mm:sszzz}');");

        // Petar: oznaka VIP + matična lokacija/trener na drugoj lokaciji.
        Execute.Sql($@"
            INSERT INTO dunelight.clients
                (id, organization_id, member_number, first_name, last_name, date_of_birth, occupation,
                 phone, email, note, health_note, gdpr_consent_given, gdpr_consent_date,
                 home_location_id, home_trainer_id, is_active, is_anonymized, created_at)
            VALUES
                ('{ClientPetar}', '{org}', 2, 'Petar', 'Perić', '1985-07-02T00:00:00+00:00', 'Poduzetnik',
                 '091 222 2222', 'petar.peric@example.com', 'Preferira jutarnje termine.', NULL,
                 true, '{now:yyyy-MM-ddTHH:mm:sszzz}',
                 (SELECT id FROM dunelight.locations WHERE organization_id = '{org}' AND name = 'Riverside'),
                 (SELECT id FROM dunelight.employees WHERE organization_id = '{org}' AND first_name = 'Iva' AND last_name = 'Vanjska'),
                 true, false, '{now:yyyy-MM-ddTHH:mm:sszzz}');");

        Insert.IntoTable(Tables.ClientTagAssignments).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            client_id = ClientPetar,
            tag_id = TagVip
        });

        // Ana: bez matičnog trenera/lokacije, bez GDPR suglasnosti.
        Insert.IntoTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = ClientAna,
            organization_id = OrganizationId,
            member_number = 3,
            first_name = "Ana",
            last_name = "Anić",
            date_of_birth = (DateTimeOffset?)null,
            occupation = (string)null,
            phone = "091 333 3333",
            email = (string)null,
            note = (string)null,
            health_note = (string)null,
            gdpr_consent_given = false,
            gdpr_consent_date = (DateTimeOffset?)null,
            home_location_id = (Guid?)null,
            home_trainer_id = (Guid?)null,
            is_active = true,
            is_anonymized = false,
            created_at = now
        });

        // Marko: oznaka Radionice, bez matičnog trenera.
        Insert.IntoTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = ClientMarko,
            organization_id = OrganizationId,
            member_number = 4,
            first_name = "Marko",
            last_name = "Novak",
            date_of_birth = new DateTimeOffset(1995, 12, 30, 0, 0, 0, TimeSpan.Zero),
            occupation = (string)null,
            phone = "091 444 4444",
            email = "marko.novak@example.com",
            note = (string)null,
            health_note = (string)null,
            gdpr_consent_given = false,
            gdpr_consent_date = (DateTimeOffset?)null,
            home_location_id = (Guid?)null,
            home_trainer_id = (Guid?)null,
            is_active = true,
            is_anonymized = false,
            created_at = now
        });

        Insert.IntoTable(Tables.ClientTagAssignments).InSchema(Tables.Schemas.DuneLight).Row(new
        {
            id = Guid.NewGuid(),
            client_id = ClientMarko,
            tag_id = TagWorkshops
        });
    }
}
