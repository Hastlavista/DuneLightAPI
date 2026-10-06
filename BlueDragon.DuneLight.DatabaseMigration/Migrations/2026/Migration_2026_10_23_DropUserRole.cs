using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// ADR-0019 — uklanja legacy users.role (UserRole Admin/Member/Reception). Autorizacija ide isključivo kroz grantove
/// (User -> UserGrantGroup -> GrantGroup -> GrantGroupGrant), pa stupac nema ni sigurnosno ni poslovno značenje.
/// Razvojna baza (ADR-0003): bez backfilla i bez Down migracije. Povijesni EmployeeAuditLog zapisi s ChangeType "Role"
/// ostaju netaknuti kao povijest.
/// </summary>
[DeveloperMigration(2026, 10, 23, Developer.SilvioHabazin, 0)]
public class DropUserRole : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($"ALTER TABLE dunelight.{Tables.Users} DROP COLUMN role;");
    }
}
