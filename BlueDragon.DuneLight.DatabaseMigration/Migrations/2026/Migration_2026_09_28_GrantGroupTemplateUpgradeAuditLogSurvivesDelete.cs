using BlueDragon.DuneLight.DatabaseMigration.Extensions;
using BlueDragon.DuneLight.DatabaseMigration.Models;

namespace BlueDragon.DuneLight.DatabaseMigration.Migrations._2026;

/// <summary>
/// Data/Lifecycle Consistency Audit — grant_group_template_upgrade_audit_log je jedini audit-log u shemi koji je
/// dobio Cascade FK na svog "vlasnika" (vidi Migration_2026_09_27_CapabilityV2Templates,
/// CreateGrantGroupTemplateUpgradeAuditLogTable); svaki drugi audit-log (RosterAuditLog, EmployeeAuditLog,
/// AppointmentAuditLog, CheckoutAuditLog, GroupAuditLog, OrganizationBrandingAuditLog) je namjerno BEZ FK-a
/// upravo zato da audit trag preživi brisanje retka na koji se odnosi (vidi komentar uz RosterAuditLog u
/// DatabaseContext.ConfigurePermissions/ConfigureRoster: "audit mora preživjeti brisanje retka"). Ovdje je to
/// bio previd, ne namjera — GrantGroup se smije trajno obrisati (GrantGroupService.Delete) kad više nema
/// dodijeljenih korisnika, a upgrade-audit povijest te grupe ne smije nestati zajedno s njom.
///
/// Miče FK/Cascade, ZADRŽAVA grant_group_id stupac i njegov indeks kao čistu povijesnu (scalar) referencu —
/// GrantGroupTemplateUpgradeAuditLogHandler/DTO-i već čitaju samo taj Guid, nikad live GrantGroup navigaciju,
/// pa nema koda koji ovisi o postojanju retka u grant_groups. Ne dira postojeće podatke.
/// </summary>
[DeveloperMigration(2026, 09, 28, Developer.SilvioHabazin, 0)]
public class RemoveGrantGroupTemplateUpgradeAuditLogCascade : DuneLightMigration
{
    public override void Up()
    {
        Delete.ForeignKey("fk_grant_group_template_upgrade_audit_log_grant_group_id")
            .OnTable(Tables.GrantGroupTemplateUpgradeAuditLog).InSchema(Tables.Schemas.DuneLight);
    }
}
