#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using BlueDragon.DuneLight.DatabaseMigration.Utils;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>
/// Phase D3B1 — Migration_2026_10_10_BookingParticipationLifecycleCutover end to end, on a throw-away database: migrate to
/// the D3A schema, seed LEGACY bookings in every lifecycle state (with version, reason and late classification), run the
/// cutover, assert every booking became exactly one participation on its appointment's segment carrying the same lifecycle
/// and that the booking lifecycle columns are gone; then roll back and assert the columns are restored. Incompatible
/// states (a booking whose appointment has two segments, pre-existing participations) make the cutover fail clearly.
/// Rows are inserted with FK triggers suspended (session_replication_role = replica), as in the D3A migration test.
/// </summary>
public class BookingParticipationLifecycleCutoverMigrationTests
{
    private const string Admin = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";
    private const long D3AVersion = 20261009000000;
    private const long D3B1Version = 20261010000000;

    private static readonly Guid Org = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("33333333-0000-0000-0000-000000000002");
    private static readonly Guid Service = Guid.Parse("33333333-0000-0000-0000-000000000003");
    private static readonly Guid AppointmentA = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid AppointmentB = Guid.Parse("44444444-0000-0000-0000-000000000002");
    private static readonly Guid SegmentA = Guid.Parse("55555555-0000-0000-0000-000000000001");
    private static readonly Guid SegmentB = Guid.Parse("55555555-0000-0000-0000-000000000002");
    private static readonly Guid Confirmed = Guid.Parse("66666666-0000-0000-0000-000000000001");
    private static readonly Guid Completed = Guid.Parse("66666666-0000-0000-0000-000000000002");
    private static readonly Guid LateCancelled = Guid.Parse("66666666-0000-0000-0000-000000000003");
    private static readonly Guid NoShow = Guid.Parse("66666666-0000-0000-0000-000000000004");

    private static void Migrate(string connectionString, long? upTo = null, long? downTo = null)
    {
        ConfigurationModel config = new() { Database = "PostgreSQL", ConnectionString = connectionString, Timeout = TimeSpan.FromMinutes(5) };
        using ServiceProvider provider = ServiceProviderGenerator.GenerateMigrationServiceProvider(config);
        IMigrationRunner runner = provider.GetRequiredService<IMigrationRunner>();
        using IMigrationScope scope = ((IMigrationScopeStarter)runner).BeginScope();
        if (downTo.HasValue)
            runner.MigrateDown(downTo.Value);
        else if (upTo.HasValue)
            runner.MigrateUp(upTo.Value);
        else
            runner.MigrateUp();
        scope.Complete();
    }

    private static async Task Exec(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<object[]>> Query(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        List<object[]> rows = new();
        while (await reader.ReadAsync())
        {
            object[] row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }

    private static async Task WithDatabase(Func<string, Task> body)
    {
        string database = $"dl_d3b1_{Guid.NewGuid():N}";
        string cs = $"Host=localhost;Database={database};Password=root1234;Username=postgres";
        await Exec(Admin, $"CREATE DATABASE {database}");
        try
        {
            Migrate(cs, upTo: D3AVersion);
            await body(cs);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(Admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    /// <summary>Two appointments with one segment each; four legacy bookings covering every lifecycle state.</summary>
    private static Task SeedLegacy(string cs) => Exec(cs, $@"
        BEGIN;
        SET LOCAL session_replication_role = replica;
        INSERT INTO dunelight.organizations (id, name, slug, created_at) VALUES ('{Org}', 'Legacy', 'legacy-d3b1', now());
        INSERT INTO dunelight.companies (id, organization_id, name, created_at) VALUES ('{Company}', '{Org}', 'C', now());
        INSERT INTO dunelight.services (id, organization_id, name, default_duration_minutes, default_price, created_at, execution_mode)
            VALUES ('{Service}', '{Org}', 'S', 30, 10, now(), 'Individual');
        INSERT INTO dunelight.appointments (id, organization_id, form, company_id, status, created_at)
            VALUES ('{AppointmentA}', '{Org}', 'Individual', '{Company}', 'Scheduled', now()),
                   ('{AppointmentB}', '{Org}', 'Group', '{Company}', 'Completed', now());
        INSERT INTO dunelight.appointment_segments (id, organization_id, appointment_id, service_id, planned_start, planned_end, created_at)
            VALUES ('{SegmentA}', '{Org}', '{AppointmentA}', '{Service}', '2031-03-03T09:00:00Z', '2031-03-03T09:30:00Z', now()),
                   ('{SegmentB}', '{Org}', '{AppointmentB}', '{Service}', '2031-03-04T09:00:00Z', '2031-03-04T09:30:00Z', now());
        INSERT INTO dunelight.bookings (id, organization_id, appointment_id, client_id, status, status_version, cancellation_reason,
                                        is_late_cancellation, amount, suggested_amount, created_at, updated_at)
            VALUES ('{Confirmed}', '{Org}', '{AppointmentA}', gen_random_uuid(), 'Confirmed', 0, NULL, NULL, 50, 50,
                    '2031-01-01T08:00:00Z', NULL),
                   ('{Completed}', '{Org}', '{AppointmentB}', gen_random_uuid(), 'Completed', 2, NULL, NULL, 15, 15,
                    '2031-01-01T08:00:00Z', '2031-01-05T08:00:00Z'),
                   ('{LateCancelled}', '{Org}', '{AppointmentB}', gen_random_uuid(), 'Cancelled', 1, 'sick', TRUE, 15, 15,
                    '2031-01-01T08:00:00Z', '2031-01-03T08:00:00Z'),
                   ('{NoShow}', '{Org}', '{AppointmentB}', gen_random_uuid(), 'NoShow', 3, 'did not come', NULL, 15, 15,
                    '2031-01-01T08:00:00Z', '2031-01-04T08:00:00Z');
        COMMIT;");

    [Fact]
    public async Task Cutover_GivesEveryBookingExactlyOneParticipationWithItsLifecycle_DropsTheBookingColumns_AndRollsBack()
    {
        await WithDatabase(async cs =>
        {
            await SeedLegacy(cs);

            Migrate(cs, upTo: D3B1Version);

            Dictionary<Guid, object[]> participations = new();
            foreach (object[] row in await Query(cs, $@"
                SELECT booking_id, id, organization_id, appointment_segment_id, status, status_version, cancellation_reason,
                       is_late_cancellation, arrived_at, arrived_by, amount, base_amount, created_at, updated_at
                  FROM dunelight.booking_segment_participations WHERE organization_id = '{Org}'"))
                participations.Add((Guid)row[0], row);

            Assert.Equal(4, participations.Count); // exactly one per booking
            // ...and every other pre-existing booking (seeded reference data) was paired the same way.
            Assert.Empty(await Query(cs, @"SELECT 1 FROM dunelight.bookings b
                WHERE (SELECT count(*) FROM dunelight.booking_segment_participations p
                        JOIN dunelight.appointment_segments s ON s.id = p.appointment_segment_id
                       WHERE p.booking_id = b.id AND s.appointment_id = b.appointment_id) <> 1"));

            object[] confirmed = participations[Confirmed];
            Assert.Equal(Org, confirmed[2]);
            Assert.Equal(SegmentA, confirmed[3]);
            Assert.Equal(("Confirmed", 0), ((string)confirmed[4], (int)confirmed[5]));
            Assert.IsType<DBNull>(confirmed[6]);
            Assert.IsType<DBNull>(confirmed[7]);
            Assert.IsType<DBNull>(confirmed[8]); // arrival has no legacy source — never invented
            Assert.IsType<DBNull>(confirmed[9]);
            Assert.IsType<DBNull>(confirmed[10]); // the pricing snapshot stays NULL: Booking is price-authoritative until D3B2
            Assert.IsType<DBNull>(confirmed[11]);
            Assert.Equal(new DateTime(2031, 1, 1, 8, 0, 0, DateTimeKind.Utc), confirmed[12]);

            object[] completed = participations[Completed];
            Assert.Equal(SegmentB, completed[3]);
            Assert.Equal(("Completed", 2), ((string)completed[4], (int)completed[5]));
            Assert.Equal(new DateTime(2031, 1, 5, 8, 0, 0, DateTimeKind.Utc), completed[13]);

            object[] late = participations[LateCancelled];
            Assert.Equal(("Cancelled", 1, "sick", true), ((string)late[4], (int)late[5], (string)late[6], (bool)late[7]));

            object[] noShow = participations[NoShow];
            Assert.Equal(("NoShow", 3, "did not come"), ((string)noShow[4], (int)noShow[5], (string)noShow[6]));
            Assert.IsType<DBNull>(noShow[7]);

            // Deterministic ids; booking lifecycle columns gone (no dual write possible).
            Assert.Single(await Query(cs, $"SELECT 1 FROM dunelight.booking_segment_participations WHERE id = md5('booking-participation:{Confirmed}')::uuid"));
            Assert.Empty(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight' AND table_name = 'bookings'
                                           AND column_name IN ('status', 'status_version', 'cancellation_reason', 'is_late_cancellation')"));

            // Rollback restores the four columns from the single participation and removes the participations.
            Migrate(cs, downTo: D3AVersion);
            Dictionary<Guid, object[]> restored = new();
            foreach (object[] row in await Query(cs, $@"
                SELECT id, status, status_version, cancellation_reason, is_late_cancellation FROM dunelight.bookings WHERE organization_id = '{Org}'"))
                restored.Add((Guid)row[0], row);
            Assert.Equal(new object[] { Confirmed, "Confirmed", 0, DBNull.Value, DBNull.Value }, restored[Confirmed]);
            Assert.Equal(new object[] { Completed, "Completed", 2, DBNull.Value, DBNull.Value }, restored[Completed]);
            Assert.Equal(new object[] { LateCancelled, "Cancelled", 1, "sick", true }, restored[LateCancelled]);
            Assert.Equal(new object[] { NoShow, "NoShow", 3, "did not come", DBNull.Value }, restored[NoShow]);
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.booking_segment_participations"));
            Assert.Equal("NO", (string)Assert.Single(await Query(cs, @"SELECT is_nullable FROM information_schema.columns
                WHERE table_schema = 'dunelight' AND table_name = 'booking_segment_participations' AND column_name = 'amount'"))[0]);
        });
    }

    [Fact]
    public async Task Cutover_RefusesABookingWhoseAppointmentHasTwoSegments()
    {
        await WithDatabase(async cs =>
        {
            await SeedLegacy(cs);
            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.appointment_segments (id, organization_id, appointment_id, service_id, planned_start, planned_end, created_at)
                    VALUES (gen_random_uuid(), '{Org}', '{AppointmentA}', '{Service}', '2031-03-03T09:30:00Z', '2031-03-03T10:00:00Z', now());
                COMMIT;");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3B1Version));

            Assert.Contains("does not have exactly one segment", ex.ToString());
            Assert.Single(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight'
                                            AND table_name = 'bookings' AND column_name = 'status'"));
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.booking_segment_participations"));
        });
    }

    [Fact]
    public async Task Cutover_RefusesAnUnexpectedPartialParticipationState()
    {
        await WithDatabase(async cs =>
        {
            await SeedLegacy(cs);
            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.booking_segment_participations (id, organization_id, booking_id, appointment_segment_id, status,
                        status_version, base_amount, base_amount_source, suggested_amount, amount, is_amount_manually_overridden, created_at)
                    VALUES (gen_random_uuid(), '{Org}', '{Confirmed}', '{SegmentA}', 'Confirmed', 0, 50, 'Default', 50, 50, FALSE, now());
                COMMIT;");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3B1Version));

            Assert.Contains("refusing to merge an unexpected partial state", ex.ToString());
            Assert.Single(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight'
                                            AND table_name = 'bookings' AND column_name = 'status'"));
        });
    }

    [Fact]
    public async Task Rollback_RefusesParticipationStateTheLegacyColumnsCannotExpress()
    {
        await WithDatabase(async cs =>
        {
            await SeedLegacy(cs);
            Migrate(cs, upTo: D3B1Version);
            await Exec(cs, $@"UPDATE dunelight.booking_segment_participations SET arrived_at = now() WHERE booking_id = '{Confirmed}'");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, downTo: D3AVersion));

            Assert.Contains("not expressible in the legacy booking lifecycle columns", ex.ToString());
            Assert.Equal(4L, (long)Assert.Single(await Query(cs, $"SELECT count(*) FROM dunelight.booking_segment_participations WHERE organization_id = '{Org}'"))[0]);
        });
    }
}
