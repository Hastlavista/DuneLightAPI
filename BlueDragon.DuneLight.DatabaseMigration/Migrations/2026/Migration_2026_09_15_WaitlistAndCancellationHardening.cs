using System.Data;
using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Poslovne postavke organizacije (1:1 s Organization) — prvi konkretan slučaj je CancellationCutoffMinutes
/// (vidi OrganizationSettings.cs). Redak je OPCIONALAN: organizacije bez retka koriste platformski default
/// (OrganizationSettingsService.DefaultCancellationCutoffMinutes = 1440) — nema potrebe za backfillom
/// postojećih organizacija.
/// </summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 16)]
public class CreateOrganizationSettingsTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.OrganizationSettings)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_organization_settings")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("cancellation_cutoff_minutes").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("created_by").AsGuid().Nullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsGuid().Nullable();

        Create.ForeignKey("fk_organization_settings_organization_id")
            .FromTable(Tables.OrganizationSettings).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_organization_settings_organization_id")
            .OnTable(Tables.OrganizationSettings).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .WithOptions().Unique();
    }
}

/// <summary>Klasifikacija trenutka otkazivanja naspram cutoffa — vidi Booking.cs domensku napomenu na
/// IsLateCancellation za točan opseg (samo klijentsko/booking-razina otkazivanje, ne NoShow ni poslovno
/// appointment-wide otkazivanje).</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 17)]
public class AddIsLateCancellationToBookings : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.Bookings)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("is_late_cancellation").AsBoolean().Nullable();
    }
}

/// <summary>Vidi WaitlistEntry.cs za punu domensku napomenu — lista čekanja je occurrence-specifična (veže se
/// na konkretan Appointment, ne na Group definiciju).</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 18)]
public class CreateWaitlistEntriesTable : DuneLightMigration
{
    public override void Up()
    {
        Create.Table(Tables.WaitlistEntries)
            .InSchema(Tables.Schemas.DuneLight)
            .WithColumn("id").AsGuid().NotNullable().PrimaryKey("pk_waitlist_entries")
            .WithColumn("organization_id").AsGuid().NotNullable()
            .WithColumn("appointment_id").AsGuid().NotNullable()
            .WithColumn("client_id").AsGuid().NotNullable()
            .WithColumn("status").AsString(20).NotNullable()
            .WithColumn("joined_at").AsDateTimeOffset().NotNullable()
            .WithColumn("promoted_at").AsDateTimeOffset().Nullable()
            .WithColumn("promoted_booking_id").AsGuid().Nullable()
            .WithColumn("cancelled_at").AsDateTimeOffset().Nullable()
            .WithColumn("expired_reason").AsString(60).Nullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().Nullable();

        Create.ForeignKey("fk_waitlist_entries_organization_id")
            .FromTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("organization_id")
            .ToTable(Tables.Organizations).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_waitlist_entries_appointment_id")
            .FromTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("appointment_id")
            .ToTable(Tables.Appointments).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(Rule.Cascade);

        Create.ForeignKey("fk_waitlist_entries_client_id")
            .FromTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("client_id")
            .ToTable(Tables.Clients).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        Create.ForeignKey("fk_waitlist_entries_promoted_booking_id")
            .FromTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight).ForeignColumn("promoted_booking_id")
            .ToTable(Tables.Bookings).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id");

        // Promocijski upit (najstariji Waiting prvi) — vidi IWaitlistService.PromoteEligibleWaiters.
        Create.Index("ix_waitlist_entries_appointment_status_joined")
            .OnTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("appointment_id").Ascending()
            .OnColumn("status").Ascending()
            .OnColumn("joined_at").Ascending();

        // Najviše JEDAN aktivan (Waiting) redak po (appointment, client) — povijesni Cancelled/Promoted/Expired
        // retci ne blokiraju ponovni upis (spec section 10).
        Execute.Sql(
            "CREATE UNIQUE INDEX ux_waitlist_entries_appointment_client_waiting " +
            "ON dunelight.waitlist_entries (appointment_id, client_id) WHERE status = 'Waiting';");

        Create.Index("ix_waitlist_entries_org_client")
            .OnTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("organization_id").Ascending()
            .OnColumn("client_id").Ascending();
    }
}

/// <summary>Audit na razini jednog WaitlistEntry retka dijeli tablicu s Appointment/Booking audit logom — isti
/// obrazac kao AddBookingIdToAppointmentAuditLog (vidi AppointmentAuditLog.cs klasnu napomenu).</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 19)]
public class AddWaitlistEntryIdToAppointmentAuditLog : DuneLightMigration
{
    public override void Up()
    {
        Alter.Table(Tables.AppointmentAuditLog)
            .InSchema(Tables.Schemas.DuneLight)
            .AddColumn("waitlist_entry_id").AsGuid().Nullable();

        Create.ForeignKey("fk_appointment_audit_log_waitlist_entry_id")
            .FromTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight).ForeignColumn("waitlist_entry_id")
            .ToTable(Tables.WaitlistEntries).InSchema(Tables.Schemas.DuneLight).PrimaryColumn("id")
            .OnDelete(Rule.Cascade);

        Create.Index("ix_appointment_audit_log_waitlist_entry_id")
            .OnTable(Tables.AppointmentAuditLog).InSchema(Tables.Schemas.DuneLight)
            .OnColumn("waitlist_entry_id").Ascending();
    }
}

/// <summary>Backfill: dodaje novi organization.settings.manage grant postojećim "Admin" GrantGroup-ama — isti
/// obrazac kao BackfillAdminBrandingGrant (nove organizacije ga dobivaju automatski preko Grants.Catalog).</summary>
[DeveloperMigration(2026, 09, 15, Developer.SilvioHabazin, 20)]
public class BackfillAdminOrganizationSettingsGrant : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            INSERT INTO dunelight.grant_group_grants (id, grant_group_id, grant_key)
            SELECT (md5(random()::text || clock_timestamp()::text || gg.id::text || 'organization.settings.manage'))::uuid,
                   gg.id, 'organization.settings.manage'
            FROM dunelight.grant_groups gg
            WHERE gg.name = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM dunelight.grant_group_grants existing
                  WHERE existing.grant_group_id = gg.id AND existing.grant_key = 'organization.settings.manage'
              );
        ");
    }
}
