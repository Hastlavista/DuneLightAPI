using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Phase M1D — rooms.capacity (osobe istovremeno, CHECK &gt;= 1) postaje JEDINO pravilo prostorije u zakazivanju (tvrdi,
/// vremenski raslojen kapacitet po segmentima). Legacy rooms.allow_concurrent_bookings (binarno "blokiraj svaki preklapajući
/// termin") nema značenje izvan tog starog pravila pa se uklanja — ne prevodi se u "ignoriraj kapacitet" niti u broj.
/// Razvojna baza (sadržaj se ne čuva). Postojeći CHECK-ovi (rooms.capacity &gt;= 1, resources.capacity &gt;= 1,
/// appointment_segment_resources.quantity_required &gt; 0) i restriktivni FK-ovi ostaju.
/// Down: stupac se vraća s izvornim zadanim false.
/// </summary>
[DeveloperMigration(2026, 10, 19, Developer.SilvioHabazin, 0)]
public class RoomCapacityAuthoritative : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql($"ALTER TABLE dunelight.{Tables.Rooms} DROP COLUMN allow_concurrent_bookings;");
    }

    public override void Down()
    {
        Execute.Sql($"ALTER TABLE dunelight.{Tables.Rooms} ADD COLUMN allow_concurrent_bookings boolean NOT NULL DEFAULT false;");
    }
}
