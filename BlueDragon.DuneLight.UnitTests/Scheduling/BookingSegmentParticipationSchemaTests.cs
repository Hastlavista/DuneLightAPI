#nullable disable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Schema produced by Migration_2026_10_08_BookingSegmentParticipations, read from the PostgreSQL catalog of the migrated
/// test database, plus checks that the bookings table is untouched and no triggers exist.
/// </summary>
public class BookingSegmentParticipationSchemaTests
{
    private static async Task<List<object[]>> Query(string sql)
    {
        await using NpgsqlConnection connection = new(SchedulingTestHost.ConnectionString);
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

    private static async Task<Dictionary<string, string>> ConstraintsOf(string table) =>
        (await Query($@"SELECT c.conname, pg_get_constraintdef(c.oid) FROM pg_constraint c
                        JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                        WHERE n.nspname = 'dunelight' AND t.relname = '{table}'"))
        .ToDictionary(r => (string)r[0], r => (string)r[1]);

    [Fact]
    public async Task Table_HasTheExpectedColumns()
    {
        Dictionary<string, (string, string)> columns = (await Query(@"
            SELECT column_name, data_type, is_nullable FROM information_schema.columns
            WHERE table_schema = 'dunelight' AND table_name = 'booking_segment_participations'"))
            .ToDictionary(r => (string)r[0], r => ((string)r[1], (string)r[2]));

        Assert.Equal(new Dictionary<string, (string, string)>
        {
            ["id"] = ("uuid", "NO"),
            ["organization_id"] = ("uuid", "NO"),
            ["booking_id"] = ("uuid", "NO"),
            ["appointment_segment_id"] = ("uuid", "NO"),
            ["status"] = ("character varying", "NO"),
            ["status_version"] = ("integer", "NO"),
            ["arrived_at"] = ("timestamp with time zone", "YES"),
            ["arrived_by"] = ("uuid", "YES"),
            ["cancellation_reason"] = ("text", "YES"),
            ["is_late_cancellation"] = ("boolean", "YES"),
            ["base_amount"] = ("numeric", "YES"),
            ["base_amount_source"] = ("character varying", "YES"),
            ["pricing_mode"] = ("character varying", "YES"), // M1G: historical pricing source used by the resolution
            ["pricing_employee_id"] = ("uuid", "YES"),
            ["adjustment_amount"] = ("numeric", "YES"),
            ["suggested_amount"] = ("numeric", "NO"),
            ["amount"] = ("numeric", "NO"),
            ["is_amount_manually_overridden"] = ("boolean", "NO"),
            ["created_at"] = ("timestamp with time zone", "NO"),
            ["updated_at"] = ("timestamp with time zone", "YES"),
        }, columns);

        // Same money representation as bookings.amount.
        Assert.Equal(new object[] { 10, 2 }, Assert.Single(await Query(@"
            SELECT numeric_precision, numeric_scale FROM information_schema.columns
            WHERE table_schema = 'dunelight' AND table_name = 'booking_segment_participations' AND column_name = 'amount'")));
    }

    [Fact]
    public async Task Constraints_RestrictParentDeletes_AndGuardStatusArrivalAndAmounts()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["pk_booking_segment_participations"] = "PRIMARY KEY (id)",
            ["fk_booking_segment_participations_organization_id"] = "FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id)",
            // No ON DELETE clause = NO ACTION: participation history is never cascaded away.
            ["fk_booking_segment_participations_booking_id"] = "FOREIGN KEY (booking_id) REFERENCES dunelight.bookings(id)",
            ["fk_booking_segment_participations_appointment_segment_id"] = "FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id)",
            ["ck_booking_segment_participations_status"] = "CHECK (((status)::text = ANY ((ARRAY['Confirmed'::character varying, 'Completed'::character varying, 'Cancelled'::character varying, 'NoShow'::character varying])::text[])))",
            ["ck_booking_segment_participations_status_version"] = "CHECK ((status_version >= 0))",
            ["ck_booking_segment_participations_arrival"] = "CHECK (((arrived_by IS NULL) OR (arrived_at IS NOT NULL)))",
            ["ck_booking_segment_participations_base_amount_source"] = "CHECK (((base_amount_source)::text = ANY ((ARRAY['EmployeeCompanySpecific'::character varying, 'EmployeeAllCompanies'::character varying, 'CompanySpecific'::character varying, 'AllCompanies'::character varying, 'Default'::character varying])::text[])))",
            ["fk_booking_segment_participations_pricing_employee_id"] = "FOREIGN KEY (pricing_employee_id) REFERENCES dunelight.employees(id) ON DELETE RESTRICT",
            ["ck_booking_segment_participations_pricing_source"] = "CHECK ((((pricing_mode IS NULL) AND (pricing_employee_id IS NULL) AND (base_amount IS NULL)) OR (((pricing_mode)::text = 'Standard'::text) AND (pricing_employee_id IS NULL) AND (base_amount IS NOT NULL)) OR (((pricing_mode)::text = 'Employee'::text) AND (pricing_employee_id IS NOT NULL) AND (base_amount IS NOT NULL))))",
            ["ck_booking_segment_participations_amounts_non_negative"] = "CHECK (((base_amount >= (0)::numeric) AND (suggested_amount >= (0)::numeric) AND (amount >= (0)::numeric)))",
        }, await ConstraintsOf("booking_segment_participations"));
    }

    [Fact]
    public async Task Indexes_AreTheUniquePairAndTheSegmentLookup()
    {
        Dictionary<string, string> indexes = (await Query(@"
            SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'dunelight' AND tablename = 'booking_segment_participations'"))
            .ToDictionary(r => (string)r[0], r => (string)r[1]);

        Assert.Equal(new[]
        {
            "ix_booking_segment_participations_appointment_segment_id", "pk_booking_segment_participations",
            "ux_booking_segment_participations_booking_segment",
            "ux_booking_segment_participations_id_booking_organization", // M0: target of the commission source FK
            "ux_booking_segment_participations_id_organization" // D3B3B: target of the tenant-safe checkout_items FK
        }, indexes.Keys.OrderBy(k => k).ToArray());
        Assert.StartsWith("CREATE UNIQUE INDEX", indexes["ux_booking_segment_participations_booking_segment"]);
        Assert.EndsWith("(booking_id, appointment_segment_id)", indexes["ux_booking_segment_participations_booking_segment"]);
        Assert.EndsWith("(appointment_segment_id)", indexes["ix_booking_segment_participations_appointment_segment_id"]);
    }

    [Fact]
    public async Task BookingsTable_IsUnchanged_AndThereAreNoTriggers()
    {
        Assert.Equal(new Dictionary<string, string>
        {
            ["pk_bookings"] = "PRIMARY KEY (id)",
            ["fk_bookings_organization_id"] = "FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id)",
            ["fk_bookings_appointment_id"] = "FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE",
            ["fk_bookings_client_id"] = "FOREIGN KEY (client_id) REFERENCES dunelight.clients(id)",
            // D3B3A: client_package_id (and its FK) is gone — package usage is the participation's PackageConsumption ledger.
        }, await ConstraintsOf("bookings"));

        Assert.Empty(await Query(@"
            SELECT 1 FROM information_schema.columns WHERE table_schema = 'dunelight' AND table_name = 'bookings'
              AND column_name IN ('appointment_segment_id', 'participation_id')"));
        Assert.Empty(await Query("SELECT 1 FROM information_schema.triggers WHERE trigger_schema = 'dunelight'"));
    }
}
