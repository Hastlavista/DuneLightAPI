#nullable disable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Schema produced by Migration_2026_10_07_AppointmentSegments, read from the PostgreSQL catalog of the migrated test
/// database, plus a check that the legacy appointments columns are untouched.
/// </summary>
public class AppointmentSegmentSchemaTests
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

    private static async Task<Dictionary<string, (string Type, string Nullable)>> Columns(string table) =>
        (await Query($@"SELECT column_name, data_type, is_nullable FROM information_schema.columns
                        WHERE table_schema = 'dunelight' AND table_name = '{table}'"))
        .ToDictionary(r => (string)r[0], r => ((string)r[1], (string)r[2]));

    /// <summary>All constraints of a table as name → definition.</summary>
    private static async Task<Dictionary<string, string>> Constraints(string table) =>
        (await Query($@"SELECT c.conname, pg_get_constraintdef(c.oid) FROM pg_constraint c
                        JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                        WHERE n.nspname = 'dunelight' AND t.relname = '{table}'"))
        .ToDictionary(r => (string)r[0], r => (string)r[1]);

    private static async Task<Dictionary<string, string>> Indexes(string table) =>
        (await Query($"SELECT indexname, indexdef FROM pg_indexes WHERE schemaname = 'dunelight' AND tablename = '{table}'"))
        .ToDictionary(r => (string)r[0], r => (string)r[1]);

    [Fact]
    public async Task AppointmentSegments_Table()
    {
        Assert.Equal(new Dictionary<string, (string, string)>
        {
            ["id"] = ("uuid", "NO"),
            ["organization_id"] = ("uuid", "NO"),
            ["appointment_id"] = ("uuid", "NO"),
            ["service_id"] = ("uuid", "NO"),
            ["planned_start"] = ("timestamp with time zone", "NO"),
            ["planned_end"] = ("timestamp with time zone", "NO"),
            ["actual_start"] = ("timestamp with time zone", "YES"),
            ["actual_end"] = ("timestamp with time zone", "YES"),
            ["room_id"] = ("uuid", "YES"),
            ["created_at"] = ("timestamp with time zone", "NO"),
            ["updated_at"] = ("timestamp with time zone", "YES"),
        }, await Columns("appointment_segments")); // no company_id: derived through the appointment

        Assert.Equal(new Dictionary<string, string>
        {
            ["pk_appointment_segments"] = "PRIMARY KEY (id)",
            ["fk_appointment_segments_organization_id"] = "FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id)",
            ["fk_appointment_segments_appointment_id"] = "FOREIGN KEY (appointment_id) REFERENCES dunelight.appointments(id) ON DELETE CASCADE",
            ["fk_appointment_segments_service_id"] = "FOREIGN KEY (service_id) REFERENCES dunelight.services(id)",
            ["fk_appointment_segments_room_id"] = "FOREIGN KEY (room_id) REFERENCES dunelight.rooms(id)",
            ["ck_appointment_segments_planned_range"] = "CHECK ((planned_end > planned_start))",
            ["ck_appointment_segments_actual_range"] = "CHECK (((actual_end IS NULL) OR ((actual_start IS NOT NULL) AND (actual_end >= actual_start))))",
        }, await Constraints("appointment_segments"));

        Dictionary<string, string> indexes = await Indexes("appointment_segments");
        Assert.Equal(new[]
        {
            "ix_appointment_segments_appointment_id", "ix_appointment_segments_organization_planned",
            "ix_appointment_segments_room_planned", "ix_appointment_segments_service_id", "pk_appointment_segments"
        }, indexes.Keys.OrderBy(k => k).ToArray());
        Assert.EndsWith("(organization_id, planned_start, planned_end)", indexes["ix_appointment_segments_organization_planned"]);
        Assert.EndsWith("(room_id, planned_start, planned_end)", indexes["ix_appointment_segments_room_planned"]);
        Assert.EndsWith("(appointment_id)", indexes["ix_appointment_segments_appointment_id"]);
        Assert.EndsWith("(service_id)", indexes["ix_appointment_segments_service_id"]);
    }

    [Fact]
    public async Task AppointmentSegmentEmployees_Table()
    {
        Assert.Equal(new Dictionary<string, (string, string)>
        {
            ["appointment_segment_id"] = ("uuid", "NO"),
            ["employee_id"] = ("uuid", "NO"),
        }, await Columns("appointment_segment_employees"));

        Assert.Equal(new Dictionary<string, string>
        {
            ["pk_appointment_segment_employees"] = "PRIMARY KEY (appointment_segment_id, employee_id)",
            ["fk_appointment_segment_employees_appointment_segment_id"] = "FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE",
            ["fk_appointment_segment_employees_employee_id"] = "FOREIGN KEY (employee_id) REFERENCES dunelight.employees(id)",
        }, await Constraints("appointment_segment_employees"));

        Dictionary<string, string> indexes = await Indexes("appointment_segment_employees");
        Assert.Equal(new[] { "ix_appointment_segment_employees_employee_id", "pk_appointment_segment_employees" }, indexes.Keys.OrderBy(k => k).ToArray());
        Assert.EndsWith("(employee_id)", indexes["ix_appointment_segment_employees_employee_id"]);
    }

    [Fact]
    public async Task AppointmentSegmentResources_Table()
    {
        Assert.Equal(new Dictionary<string, (string, string)>
        {
            ["appointment_segment_id"] = ("uuid", "NO"),
            ["resource_id"] = ("uuid", "NO"),
            ["quantity_required"] = ("integer", "NO"),
        }, await Columns("appointment_segment_resources"));

        Assert.Equal(new Dictionary<string, string>
        {
            ["pk_appointment_segment_resources"] = "PRIMARY KEY (appointment_segment_id, resource_id)",
            ["fk_appointment_segment_resources_appointment_segment_id"] = "FOREIGN KEY (appointment_segment_id) REFERENCES dunelight.appointment_segments(id) ON DELETE CASCADE",
            ["fk_appointment_segment_resources_resource_id"] = "FOREIGN KEY (resource_id) REFERENCES dunelight.resources(id)",
            ["ck_appointment_segment_resources_quantity_positive"] = "CHECK ((quantity_required > 0))",
        }, await Constraints("appointment_segment_resources"));

        Dictionary<string, string> indexes = await Indexes("appointment_segment_resources");
        Assert.Equal(new[] { "ix_appointment_segment_resources_resource_id", "pk_appointment_segment_resources" }, indexes.Keys.OrderBy(k => k).ToArray());
        Assert.EndsWith("(resource_id)", indexes["ix_appointment_segment_resources_resource_id"]);
    }

    [Fact]
    public async Task LegacyAppointmentFrameColumns_AreRemoved()
    {
        // CHANGED in D3A (was LegacyAppointmentColumns_AreUnchanged): the frame lives only on the segment now.
        Dictionary<string, (string Type, string Nullable)> columns = await Columns("appointments");

        foreach (string legacy in new[] { "service_id", "employee_id", "room_id", "starts_at", "duration_minutes" })
            Assert.DoesNotContain(legacy, columns.Keys);
        Assert.Equal(("uuid", "NO"), columns["company_id"]);
        Assert.Empty(await Query("SELECT 1 FROM information_schema.triggers WHERE trigger_schema = 'dunelight' AND event_object_table LIKE 'appointment%'"));
    }
}
