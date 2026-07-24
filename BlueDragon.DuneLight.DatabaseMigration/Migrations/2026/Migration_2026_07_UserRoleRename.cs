using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// UserRole enum preimenovan (Owner→Admin, Receptionist→Reception) da bude identičan
/// role-claimu koji izlazi iz Login/CreateWithLogin ugovora (Admin/Member/Reception svugdje,
/// bez odvojenog prijevoda). Ovdje se samo prepisuju već upisani retci iz stare seed migracije.
/// </summary>
[DeveloperMigration(2026, 07, 22, Developer.SilvioHabazin, 41)]
public class RenameUserRoleOwnerAndReceptionist : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql("UPDATE dunelight.users SET role = 'Admin' WHERE role = 'Owner';");
        Execute.Sql("UPDATE dunelight.users SET role = 'Reception' WHERE role = 'Receptionist';");
    }
}
