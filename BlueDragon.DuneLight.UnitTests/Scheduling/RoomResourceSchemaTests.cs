#nullable disable
using System.Collections.Generic;
using System.Threading.Tasks;
using Npgsql;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Schema produced by Migration_2026_10_06_RoomCapacityAndResources, read from the PostgreSQL catalog of the migrated
/// test database: rooms.capacity (NOT NULL, no default, CHECK &gt;= 1, legacy flag kept) and the resources table (FKs,
/// CHECK, active-name unique index).
/// </summary>
public class RoomResourceSchemaTests
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

    private static async Task<object[]> Column(string table, string column) => Assert.Single(await Query($@"
        SELECT data_type, is_nullable, column_default FROM information_schema.columns
        WHERE table_schema = 'dunelight' AND table_name = '{table}' AND column_name = '{column}'"));

    private static async Task<string> ConstraintDefinition(string name) => (string)Assert.Single(await Query($@"
        SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE n.nspname = 'dunelight' AND c.conname = '{name}'"))[0];

    [Fact]
    public async Task Rooms_Capacity_IsRequiredWithoutDefault_AndChecked()
    {
        object[] capacity = await Column("rooms", "capacity");
        Assert.Equal("integer", capacity[0]);
        Assert.Equal("NO", capacity[1]);
        Assert.IsType<System.DBNull>(capacity[2]); // the technical migration default was dropped

        Assert.Equal("CHECK ((capacity >= 1))", await ConstraintDefinition("ck_rooms_capacity_positive"));

        object[] legacyFlag = await Column("rooms", "allow_concurrent_bookings");
        Assert.Equal("boolean", legacyFlag[0]);
    }

    [Fact]
    public async Task Resources_Table_HasTheExpectedColumns()
    {
        Dictionary<string, (string Type, string Nullable)> columns = new();
        foreach (object[] row in await Query(@"
            SELECT column_name, data_type, is_nullable FROM information_schema.columns
            WHERE table_schema = 'dunelight' AND table_name = 'resources'"))
            columns[(string)row[0]] = ((string)row[1], (string)row[2]);

        Assert.Equal(new Dictionary<string, (string, string)>
        {
            ["id"] = ("uuid", "NO"),
            ["organization_id"] = ("uuid", "NO"),
            ["company_id"] = ("uuid", "NO"),
            ["name"] = ("character varying", "NO"),
            ["capacity"] = ("integer", "NO"),
            ["is_active"] = ("boolean", "NO"),
            ["note"] = ("text", "YES"),
            ["sort_order"] = ("integer", "NO"),
            ["created_at"] = ("timestamp with time zone", "NO"),
            ["created_by"] = ("uuid", "YES"),
            ["updated_at"] = ("timestamp with time zone", "YES"),
            ["updated_by"] = ("uuid", "YES"),
        }, columns);

        Assert.IsType<System.DBNull>((await Column("resources", "capacity"))[2]);
    }

    [Fact]
    public async Task Resources_HasCapacityCheck_ForeignKeys_AndActiveNameUniqueIndex()
    {
        Assert.Equal("CHECK ((capacity >= 1))", await ConstraintDefinition("ck_resources_capacity_positive"));
        Assert.Equal("FOREIGN KEY (company_id) REFERENCES dunelight.companies(id)", await ConstraintDefinition("fk_resources_company_id"));
        Assert.Equal("FOREIGN KEY (organization_id) REFERENCES dunelight.organizations(id)", await ConstraintDefinition("fk_resources_organization_id"));
        Assert.Equal("PRIMARY KEY (id)", await ConstraintDefinition("pk_resources"));

        string unique = (string)Assert.Single(await Query(@"
            SELECT indexdef FROM pg_indexes WHERE schemaname = 'dunelight' AND indexname = 'ux_resources_org_company_name_active'"))[0];
        string roomUnique = (string)Assert.Single(await Query(@"
            SELECT indexdef FROM pg_indexes WHERE schemaname = 'dunelight' AND indexname = 'ux_rooms_org_company_name_active'"))[0];

        Assert.StartsWith("CREATE UNIQUE INDEX ux_resources_org_company_name_active ON dunelight.resources", unique);
        // Same uniqueness model as Room: (organization, company, normalized name) among active rows.
        Assert.Equal(roomUnique.Replace("rooms", "resources"), unique);

        Assert.Single(await Query(@"
            SELECT 1 FROM pg_indexes WHERE schemaname = 'dunelight' AND indexname = 'ix_resources_organization_company'"));
    }
}
