using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (pregled 2C): (6) poništavanje prodaje prebacuje i otpisana zaduženja u Voided, a zapis otpisa ostaje u povijesti
/// zaduženja — CHECK dopušta written_off_* uz lifecycle Voided (izvještaj otpisa broji samo lifecycle WrittenOff);
/// (8) minimalna obveza nakon promjene uvjeta: commitment_from_on (od kad se broje periodi obveze trenutnih uvjeta) i
/// commitment_floor_on (dosadašnji kraj obveze; kraj obveze je kasniji od ta dva). Lokalna razvojna baza ima samo testne
/// retke (ADR-0003): postojeći dobivaju commitment_from_on = starts_on.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 8)]
public class P2ReviewOfPhase2C : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.membership_charges DROP CONSTRAINT ck_membership_charges_written_off;
            ALTER TABLE dunelight.membership_charges ADD CONSTRAINT ck_membership_charges_written_off CHECK (((((lifecycle)::text = 'WrittenOff'::text) AND (written_off_at IS NOT NULL) AND (write_off_reason IS NOT NULL)) OR (((lifecycle)::text = 'Open'::text) AND (written_off_at IS NULL)) OR ((lifecycle)::text = 'Voided'::text)));");

        Execute.Sql(@"
            ALTER TABLE dunelight.client_memberships ADD COLUMN commitment_from_on date;
            UPDATE dunelight.client_memberships SET commitment_from_on = starts_on;
            ALTER TABLE dunelight.client_memberships ALTER COLUMN commitment_from_on SET NOT NULL;
            ALTER TABLE dunelight.client_memberships ADD COLUMN commitment_floor_on date;
            ALTER TABLE dunelight.client_memberships ADD CONSTRAINT ck_client_memberships_commitment_from CHECK ((commitment_from_on >= starts_on));");
    }
}
