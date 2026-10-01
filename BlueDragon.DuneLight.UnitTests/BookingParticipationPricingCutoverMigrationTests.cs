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
/// Phase D3B2 — Migration_2026_10_11_BookingParticipationPricingCutover end to end, on a throw-away database: seed
/// legacy bookings at the D3A schema (suggested-price, manual override, zero price), migrate through D3B1 (one
/// participation per booking) to D3B2, assert the three price fields were copied EXACTLY, the historical resolution
/// snapshot stayed NULL (never fabricated), the columns are NOT NULL and the Booking price columns are gone; then roll
/// back and assert identical Booking values. Invalid states (two participations, pre-existing participation pricing,
/// an unrepresentable negative price) make the cutover fail clearly.
/// </summary>
public class BookingParticipationPricingCutoverMigrationTests
{
    private const string Admin = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";
    private const long D3AVersion = 20261009000000;
    private const long D3B1Version = 20261010000000;
    private const long D3B2Version = 20261011000000;

    private static readonly Guid Org = Guid.Parse("77777777-0000-0000-0000-000000000001");
    private static readonly Guid Company = Guid.Parse("77777777-0000-0000-0000-000000000002");
    private static readonly Guid Service = Guid.Parse("77777777-0000-0000-0000-000000000003");
    private static readonly Guid Appointment = Guid.Parse("88888888-0000-0000-0000-000000000001");
    private static readonly Guid Segment = Guid.Parse("88888888-0000-0000-0000-000000000002");
    private static readonly Guid AtSuggested = Guid.Parse("99999999-0000-0000-0000-000000000001");
    private static readonly Guid Overridden = Guid.Parse("99999999-0000-0000-0000-000000000002");
    private static readonly Guid Free = Guid.Parse("99999999-0000-0000-0000-000000000003");

    private static void Migrate(string connectionString, long? upTo = null, long? downTo = null)
    {
        ConfigurationModel config = new() { Database = "PostgreSQL", ConnectionString = connectionString, Timeout = TimeSpan.FromMinutes(5) };
        using ServiceProvider provider = ServiceProviderGenerator.GenerateMigrationServiceProvider(config);
        IMigrationRunner runner = provider.GetRequiredService<IMigrationRunner>();
        using IMigrationScope scope = ((IMigrationScopeStarter)runner).BeginScope();
        if (downTo.HasValue)
            runner.MigrateDown(downTo.Value);
        else
            runner.MigrateUp(upTo.Value);
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

    /// <summary>Throw-away database at the D3B1 schema with three legacy-priced bookings (seeded at D3A, then cut over).</summary>
    private static async Task WithD3B1Database(Func<string, Task> body, string extraLegacySql = "")
    {
        string database = $"dl_d3b2_{Guid.NewGuid():N}";
        string cs = $"Host=localhost;Database={database};Password=root1234;Username=postgres";
        await Exec(Admin, $"CREATE DATABASE {database}");
        try
        {
            Migrate(cs, upTo: D3AVersion);
            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.organizations (id, name, slug, created_at) VALUES ('{Org}', 'Legacy', 'legacy-d3b2', now());
                INSERT INTO dunelight.companies (id, organization_id, name, created_at) VALUES ('{Company}', '{Org}', 'C', now());
                INSERT INTO dunelight.services (id, organization_id, name, default_duration_minutes, default_price, created_at, execution_mode)
                    VALUES ('{Service}', '{Org}', 'S', 30, 10, now(), 'Individual');
                INSERT INTO dunelight.appointments (id, organization_id, form, company_id, status, created_at)
                    VALUES ('{Appointment}', '{Org}', 'Individual', '{Company}', 'Scheduled', now());
                INSERT INTO dunelight.appointment_segments (id, organization_id, appointment_id, service_id, planned_start, planned_end, created_at)
                    VALUES ('{Segment}', '{Org}', '{Appointment}', '{Service}', '2031-03-03T09:00:00Z', '2031-03-03T09:30:00Z', now());
                INSERT INTO dunelight.bookings (id, organization_id, appointment_id, client_id, status, status_version,
                                                amount, suggested_amount, is_amount_manually_overridden, created_at)
                    VALUES ('{AtSuggested}', '{Org}', '{Appointment}', gen_random_uuid(), 'Confirmed', 0, 50.00, 50.00, FALSE, now()),
                           ('{Overridden}', '{Org}', '{Appointment}', gen_random_uuid(), 'Completed', 1, 37.25, 50.00, TRUE, now()),
                           ('{Free}', '{Org}', '{Appointment}', gen_random_uuid(), 'Confirmed', 0, 0.00, 0.00, FALSE, now());
                {extraLegacySql}
                COMMIT;");
            Migrate(cs, upTo: D3B1Version);
            await body(cs);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await Exec(Admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task<Dictionary<Guid, object[]>> Rows(string cs, string sql)
    {
        Dictionary<Guid, object[]> rows = new();
        foreach (object[] row in await Query(cs, sql))
            rows.Add((Guid)row[0], row);
        return rows;
    }

    [Fact]
    public async Task Cutover_CopiesThePriceExactly_LeavesHistoryUnknown_DropsTheBookingColumns_AndRollsBackIdentically()
    {
        await WithD3B1Database(async cs =>
        {
            Dictionary<Guid, object[]> before = await Rows(cs, $@"
                SELECT id, amount, suggested_amount, is_amount_manually_overridden FROM dunelight.bookings WHERE organization_id = '{Org}'");

            Migrate(cs, upTo: D3B2Version);

            Dictionary<Guid, object[]> participations = await Rows(cs, $@"
                SELECT booking_id, amount, suggested_amount, is_amount_manually_overridden, base_amount, base_amount_source,
                       adjustment_amount, status, status_version
                  FROM dunelight.booking_segment_participations WHERE organization_id = '{Org}'");
            Assert.Equal(3, participations.Count);
            Assert.Equal(new object[] { 50.00m, 50.00m, false }, participations[AtSuggested][1..4]);
            Assert.Equal(new object[] { 37.25m, 50.00m, true }, participations[Overridden][1..4]);
            Assert.Equal(new object[] { 0.00m, 0.00m, false }, participations[Free][1..4]);
            foreach (object[] p in participations.Values)
            {
                // Historical explainability is not fabricated: no BaseAmount = SuggestedAmount, no Suggested - Amount adjustment.
                Assert.IsType<DBNull>(p[4]);
                Assert.IsType<DBNull>(p[5]);
                Assert.IsType<DBNull>(p[6]);
            }
            Assert.Equal(("Completed", 1), ((string)participations[Overridden][7], (int)participations[Overridden][8])); // lifecycle untouched

            Assert.Empty(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight' AND table_name = 'bookings'
                                           AND column_name IN ('amount', 'suggested_amount', 'is_amount_manually_overridden')"));
            Assert.Equal(new[] { "NO", "NO", "NO" }, (await Query(cs, @"SELECT is_nullable FROM information_schema.columns
                WHERE table_schema = 'dunelight' AND table_name = 'booking_segment_participations'
                  AND column_name IN ('amount', 'suggested_amount', 'is_amount_manually_overridden') ORDER BY column_name"))
                .ConvertAll(r => (string)r[0]));

            Migrate(cs, downTo: D3B1Version);

            Dictionary<Guid, object[]> after = await Rows(cs, $@"
                SELECT id, amount, suggested_amount, is_amount_manually_overridden FROM dunelight.bookings WHERE organization_id = '{Org}'");
            Assert.Equal(before.Count, after.Count);
            foreach (Guid id in before.Keys)
                Assert.Equal(before[id], after[id]);
            Assert.Empty(await Query(cs, "SELECT 1 FROM dunelight.booking_segment_participations WHERE amount IS NOT NULL OR base_amount IS NOT NULL"));
        });
    }

    [Fact]
    public async Task Rollback_DiscardsOnlyTheResolutionSnapshot_TheLegacyRepresentableFieldsComeBack()
    {
        await WithD3B1Database(async cs =>
        {
            Migrate(cs, upTo: D3B2Version);
            // A newly-priced participation (post-cutover) carries a resolution snapshot the old schema cannot hold.
            await Exec(cs, $@"UPDATE dunelight.booking_segment_participations SET amount = 44, suggested_amount = 44,
                              base_amount = 44, base_amount_source = 'CompanySpecific' WHERE booking_id = '{AtSuggested}'");

            Migrate(cs, downTo: D3B1Version);

            Assert.Equal(new object[] { 44.00m, 44.00m, false }, Assert.Single(await Query(cs, $@"
                SELECT amount, suggested_amount, is_amount_manually_overridden FROM dunelight.bookings WHERE id = '{AtSuggested}'")));
        });
    }

    [Fact]
    public async Task Cutover_RefusesABookingWithTwoParticipations()
    {
        await WithD3B1Database(async cs =>
        {
            await Exec(cs, $@"
                BEGIN;
                SET LOCAL session_replication_role = replica;
                INSERT INTO dunelight.appointment_segments (id, organization_id, appointment_id, service_id, planned_start, planned_end, created_at)
                    VALUES ('{Guid.Empty.ToString().Replace('0', 'a')}', '{Org}', '{Appointment}', '{Service}', '2031-03-03T09:30:00Z', '2031-03-03T10:00:00Z', now());
                INSERT INTO dunelight.booking_segment_participations (id, organization_id, booking_id, appointment_segment_id, status, status_version, created_at)
                    VALUES (gen_random_uuid(), '{Org}', '{AtSuggested}', '{Guid.Empty.ToString().Replace('0', 'a')}', 'Confirmed', 0, now());
                COMMIT;");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3B2Version));

            Assert.Contains("does not have exactly one participation", ex.ToString());
            Assert.Single(await Query(cs, @"SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight'
                                            AND table_name = 'bookings' AND column_name = 'amount'"));
        });
    }

    [Fact]
    public async Task Cutover_RefusesAParticipationThatAlreadyCarriesPricing()
    {
        await WithD3B1Database(async cs =>
        {
            await Exec(cs, $"UPDATE dunelight.booking_segment_participations SET amount = 1 WHERE booking_id = '{Free}'");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3B2Version));

            Assert.Contains("refusing to merge an unexpected partial state", ex.ToString());
        });
    }

    [Fact]
    public async Task Cutover_RefusesANegativeBookingPrice_ThatTheParticipationCannotRepresent()
    {
        await WithD3B1Database(async cs =>
        {
            await Exec(cs, $"UPDATE dunelight.bookings SET amount = -5 WHERE id = '{Overridden}'");

            Exception ex = Assert.ThrowsAny<Exception>(() => Migrate(cs, upTo: D3B2Version));

            Assert.Contains("cannot be represented on its participation", ex.ToString());
            Assert.Equal(-5.00m, (decimal)Assert.Single(await Query(cs, $"SELECT amount FROM dunelight.bookings WHERE id = '{Overridden}'"))[0]);
        });
    }
}
