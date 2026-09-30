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
/// Phase D3A — Migration_2026_10_09_AppointmentSegmentCutover end to end, on a throw-away database: migrate to the D2
/// schema, seed LEGACY appointment rows (with and without employee/room, non-UTC inputs), run the cutover, assert every
/// appointment became exactly one equivalent segment, then roll back and assert the legacy columns are restored.
/// Catalog rows are inserted with FK triggers suspended (session_replication_role = replica) so the test does not need a
/// full user/engagement-type graph just to own an employee row.
/// </summary>
public class AppointmentSegmentCutoverMigrationTests
{
    private const string Admin = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";
    private const long D2Version = 20261008000000;
    private const long D3AVersion = 20261009000000;

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

    private static readonly Guid Org = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("11111111-0000-0000-0000-000000000002");
    private static readonly Guid Service = Guid.Parse("11111111-0000-0000-0000-000000000003");
    private static readonly Guid Employee = Guid.Parse("11111111-0000-0000-0000-000000000004");
    private static readonly Guid Room = Guid.Parse("11111111-0000-0000-0000-000000000005");
    private static readonly Guid WithEverything = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Trainerless = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid WithoutRoom = Guid.Parse("22222222-0000-0000-0000-000000000003");

    [Fact]
    public async Task Cutover_TurnsEveryLegacyAppointmentIntoExactlyOneEquivalentSegment_AndRollsBack()
    {
        string database = $"dl_d3a_{Guid.NewGuid():N}";
        string cs = $"Host=localhost;Database={database};Password=root1234;Username=postgres";
        await Exec(Admin, $"CREATE DATABASE {database}");
        try
        {
            Migrate(cs, upTo: D2Version);

            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.organizations (id, name, slug, created_at) VALUES ('{Org}', 'Legacy', 'legacy', now());
                INSERT INTO dunelight.companies (id, organization_id, name, created_at) VALUES ('{Company}', '{Org}', 'C', now());
                INSERT INTO dunelight.services (id, organization_id, name, default_duration_minutes, default_price, created_at, execution_mode)
                    VALUES ('{Service}', '{Org}', 'S', 30, 10, now(), 'Individual');
                INSERT INTO dunelight.employees (id, organization_id, first_name, last_name, employment_start_date, engagement_type_id, user_id, created_at)
                    VALUES ('{Employee}', '{Org}', 'E', 'E', now(), gen_random_uuid(), gen_random_uuid(), now());
                INSERT INTO dunelight.rooms (id, organization_id, company_id, name, created_at, capacity) VALUES ('{Room}', '{Org}', '{Company}', 'R', now(), 4);
                INSERT INTO dunelight.appointments (id, organization_id, form, company_id, status, created_at, updated_at,
                                                    service_id, employee_id, room_id, starts_at, duration_minutes)
                VALUES
                    ('{WithEverything}', '{Org}', 'Individual', '{Company}', 'Scheduled', '2031-01-01T08:00:00Z', '2031-01-02T08:00:00Z',
                     '{Service}', '{Employee}', '{Room}', '2031-03-03T11:00:00+02:00', 45),
                    ('{Trainerless}', '{Org}', 'Group', '{Company}', 'Completed', '2031-01-01T08:00:00Z', NULL,
                     '{Service}', NULL, '{Room}', '2031-03-30T01:30:00Z', 60),
                    ('{WithoutRoom}', '{Org}', 'Individual', '{Company}', 'Cancelled', '2031-01-01T08:00:00Z', NULL,
                     '{Service}', '{Employee}', NULL, '2031-10-26T00:30:00Z', 30);
                COMMIT;");

            Migrate(cs, upTo: D3AVersion);

            Dictionary<Guid, object[]> segments = new();
            foreach (object[] row in await Query(cs, $@"
                SELECT s.appointment_id, s.id, s.organization_id, s.service_id, s.room_id, s.planned_start, s.planned_end,
                       s.actual_start, s.actual_end, s.created_at, s.updated_at,
                       (SELECT count(*) FROM dunelight.appointment_segment_employees e WHERE e.appointment_segment_id = s.id),
                       (SELECT min(e.employee_id::text) FROM dunelight.appointment_segment_employees e WHERE e.appointment_segment_id = s.id)
                  FROM dunelight.appointment_segments s WHERE s.organization_id = '{Org}'"))
                segments.Add((Guid)row[0], row);

            Assert.Equal(3, segments.Count); // exactly one per appointment of this organization
            // ...and every other pre-existing appointment (seeded reference data) was converted the same way.
            Assert.Empty(await Query(cs, @"SELECT 1 FROM dunelight.appointments a
                WHERE (SELECT count(*) FROM dunelight.appointment_segments s WHERE s.appointment_id = a.id) <> 1"));

            object[] full = segments[WithEverything];
            Assert.Equal(Org, full[2]);
            Assert.Equal(Service, full[3]);
            Assert.Equal(Room, full[4]);
            Assert.Equal(new DateTime(2031, 3, 3, 9, 0, 0, DateTimeKind.Utc), full[5]); // same instant as 11:00+02:00
            Assert.Equal(new DateTime(2031, 3, 3, 9, 45, 0, DateTimeKind.Utc), full[6]);
            Assert.IsType<DBNull>(full[7]);
            Assert.IsType<DBNull>(full[8]);
            Assert.Equal(new DateTime(2031, 1, 1, 8, 0, 0, DateTimeKind.Utc), full[9]);
            Assert.Equal(new DateTime(2031, 1, 2, 8, 0, 0, DateTimeKind.Utc), full[10]);
            Assert.Equal(1L, full[11]);
            Assert.Equal(Employee.ToString(), full[12]);

            object[] trainerless = segments[Trainerless];
            Assert.Equal(Room, trainerless[4]);
            Assert.Equal(new DateTime(2031, 3, 30, 1, 30, 0, DateTimeKind.Utc), trainerless[5]);
            Assert.Equal(new DateTime(2031, 3, 30, 2, 30, 0, DateTimeKind.Utc), trainerless[6]); // plain UTC arithmetic across the Zagreb DST gap
            Assert.Equal(0L, trainerless[11]);

            object[] withoutRoom = segments[WithoutRoom];
            Assert.IsType<DBNull>(withoutRoom[4]);
            Assert.Equal(new DateTime(2031, 10, 26, 1, 0, 0, DateTimeKind.Utc), withoutRoom[6]);
            Assert.Equal(1L, withoutRoom[11]);

            // Deterministic ids, no resources, no participations, legacy columns gone.
            Assert.Single(await Query(cs, $"SELECT 1 FROM dunelight.appointment_segments WHERE id = md5('appointment-segment:{WithEverything}')::uuid"));
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.appointment_segment_resources"));
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.booking_segment_participations"));
            Assert.Empty(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight' AND table_name = 'appointments'
                                           AND column_name IN ('service_id', 'employee_id', 'room_id', 'starts_at', 'duration_minutes')"));

            // Rollback restores the legacy frame from the single segment.
            Migrate(cs, downTo: D2Version);
            object[] restored = Assert.Single(await Query(cs, $@"
                SELECT service_id, employee_id, room_id, starts_at, duration_minutes FROM dunelight.appointments WHERE id = '{WithEverything}'"));
            Assert.Equal(new object[] { Service, Employee, Room, new DateTime(2031, 3, 3, 9, 0, 0, DateTimeKind.Utc), 45 }, restored);
            Assert.IsType<DBNull>(Assert.Single(await Query(cs, $"SELECT employee_id FROM dunelight.appointments WHERE id = '{Trainerless}'"))[0]);
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.appointment_segments"));
            Assert.Single(await Query(cs, "SELECT 1 FROM pg_indexes WHERE indexname = 'ux_appointments_group_slot_startsat'"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(Admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    [Fact]
    public async Task Cutover_RefusesAnUnexpectedPartialSegmentState()
    {
        string database = $"dl_d3a_{Guid.NewGuid():N}";
        string cs = $"Host=localhost;Database={database};Password=root1234;Username=postgres";
        await Exec(Admin, $"CREATE DATABASE {database}");
        try
        {
            Migrate(cs, upTo: D2Version);
            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.appointment_segments (id, organization_id, appointment_id, service_id, planned_start, planned_end, created_at)
                    VALUES (gen_random_uuid(), '{Org}', '{WithEverything}', '{Service}', now(), now() + interval '30 minutes', now());
                COMMIT;");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3AVersion));

            Assert.Contains("refusing to merge an unexpected partial state", ex.ToString());
            Assert.Single(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight'
                                            AND table_name = 'appointments' AND column_name = 'starts_at'"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(Admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }
}
