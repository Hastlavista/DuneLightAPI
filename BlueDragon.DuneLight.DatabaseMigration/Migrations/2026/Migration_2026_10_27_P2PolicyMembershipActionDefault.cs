using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// P2 (pregled 2D, izbor #8): ForfeitCredit uz događaj BEZ naknade troši kredit samo ako ga je organizacija izričito odabrala;
/// bez izbora takav događaj ne kažnjava ni članove (ReturnCreditChargeFee uz naknadu 0 = kredit se vraća). Vrijednost se uvijek
/// postavlja eksplicitno pri nastanku verzije (CancellationPolicyRules.MembershipActionFor), pa se uklanja default stupca.
/// Postojeće verzije bez naknade (lokalna razvojna baza ima samo testne retke, ADR-0003) prelaze na ReturnCreditChargeFee;
/// snapshotovi na već nastalim posljedicama se ne mijenjaju.
/// </summary>
[DeveloperMigration(2026, 10, 27, Developer.SilvioHabazin, 11)]
public class P2PolicyMembershipActionDefault : DuneLightMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            ALTER TABLE dunelight.cancellation_policy_versions
                ALTER COLUMN late_cancellation_membership_action DROP DEFAULT,
                ALTER COLUMN no_show_membership_action DROP DEFAULT;
            UPDATE dunelight.cancellation_policy_versions
                SET late_cancellation_membership_action = 'ReturnCreditChargeFee'
                WHERE late_cancellation_fee_type = 'None' OR late_cancellation_fee_value = 0;
            UPDATE dunelight.cancellation_policy_versions
                SET no_show_membership_action = 'ReturnCreditChargeFee'
                WHERE no_show_fee_type = 'None' OR no_show_fee_value = 0;");
    }
}
