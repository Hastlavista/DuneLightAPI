using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Residual IsOwner Removal — users.is_owner je od Grant-only Tenant Authorization Refactora (vidi
/// Migration_2026_09_30_BackfillOwnersIntoPermissionAdminGrantGroup) potpuno inertan za autorizaciju: GrantContext
/// više uopće ne čita ovaj stupac, RequireOwner/ownerGuard su uklonjeni, a svaki dosadašnji aktivni Owner korisnik
/// je backfillanom migracijom već dobio stvarnu GrantGroup dodjelu s permissions.manage (verificirano — svih 4
/// postojeće organizacije imaju točno jednog aktivnog korisnika s efektivnim permissions.manage). Stupac se briše
/// tek SADA (posebna migracija, ne prepisuje Migration_2026_08_GrantSystem koja ga je dodala) da backfill korak
/// ostane potpuno odvojen i provjerljiv od trenutka brisanja — isti "dvofazni" obrazac kao i sam FAZA 1/2 rad.
/// </summary>
[DeveloperMigration(2026, 10, 01, Developer.SilvioHabazin, 0)]
public class DropUsersIsOwnerColumn : DuneLightMigration
{
    public override void Up()
    {
        Delete.Column("is_owner").FromTable(Tables.Users).InSchema(Tables.Schemas.DuneLight);
    }
}
