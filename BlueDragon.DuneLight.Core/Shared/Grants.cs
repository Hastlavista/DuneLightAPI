using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za sve grant-ključeve u sustavu. Grantovi su definirani U KODU (ne u bazi) —
/// GrantGroup u bazi samo bira podskup ovih ključeva. Ne mijenjati postojeće vrijednosti (GrantGroup
/// zapisi u bazi ih referenciraju kao stringove), samo dodavati nove.
/// </summary>
public static class Grants
{
    public const string EmployeesDirectoryView = "employees.directory.view";
    public const string EmployeesView = "employees.view";
    public const string EmployeesManage = "employees.manage";
    public const string EmployeesRoleManage = "employees.role.manage";
    public const string EmployeesEngagementTypesView = "employees.engagement-types.view";
    public const string EmployeesEngagementTypesManage = "employees.engagement-types.manage";

    public const string CatalogCompaniesView = "catalog.companies.view";
    public const string CatalogCompaniesManage = "catalog.companies.manage";
    public const string CatalogServicesView = "catalog.services.view";
    public const string CatalogServicesManage = "catalog.services.manage";
    public const string CatalogPackagesView = "catalog.packages.view";
    public const string CatalogPackagesManage = "catalog.packages.manage";
    public const string CatalogPriceListView = "catalog.price-list.view";
    public const string CatalogPriceListManage = "catalog.price-list.manage";

    public const string ClientsView = "clients.view";
    public const string ClientsManage = "clients.manage";
    public const string ClientsStatusManage = "clients.status.manage";
    public const string ClientsAnonymize = "clients.anonymize";
    public const string ClientsTagsView = "clients.tags.view";
    public const string ClientsTagsManage = "clients.tags.manage";
    public const string ClientsPackagesView = "clients.packages.view";
    public const string ClientsPackagesManage = "clients.packages.manage";

    public const string AppointmentsView = "appointments.view";
    public const string AppointmentsWriteOwn = "appointments.write.own";
    public const string AppointmentsWriteAll = "appointments.write.all";
    public const string AppointmentsDelete = "appointments.delete";

    public const string ScheduleBreaksView = "schedule.breaks.view";
    public const string ScheduleBreaksWriteOwn = "schedule.breaks.write.own";
    public const string ScheduleBreaksWriteAll = "schedule.breaks.write.all";

    public const string GroupsView = "groups.view";
    public const string GroupsManage = "groups.manage";
    public const string GroupsAttendanceView = "groups.attendance.view";
    public const string GroupsAttendanceOwn = "groups.attendance.own";
    public const string GroupsAttendanceAll = "groups.attendance.all";

    public const string RosterTypesView = "roster.types.view";
    public const string RosterTypesManage = "roster.types.manage";
    public const string RosterEntriesView = "roster.entries.view";
    public const string RosterEntriesWriteOwn = "roster.entries.write.own";
    public const string RosterEntriesWriteAll = "roster.entries.write.all";
    public const string RosterReviewsTeamView = "roster.reviews.team.view";
    public const string RosterReviewsPersonalViewOwn = "roster.reviews.personal.view.own";
    public const string RosterReviewsPersonalViewAll = "roster.reviews.personal.view.all";

    /// <summary>Namjerno bez own/all podjele — predložak generira tvrdu blokadu zakazivanja pa se ne prepušta zaposleniku (vidi FAZA 1).</summary>
    public const string RosterTemplatesView = "roster.templates.view";
    public const string RosterTemplatesManage = "roster.templates.manage";

    /// <summary>Namjerno bez own/all podjele, isto obrazloženje kao RosterTemplates — admin postavlja fond za sve, nema samoposluživanja.</summary>
    public const string RosterLeaveFundSettingsView = "roster.leave-fund.settings.view";
    public const string RosterLeaveFundSettingsManage = "roster.leave-fund.settings.manage";
    public const string RosterLeaveFundViewOwn = "roster.leave-fund.view.own";
    public const string RosterLeaveFundViewAll = "roster.leave-fund.view.all";
    public const string RosterLeaveFundManage = "roster.leave-fund.manage";

    /// <summary>Puni katalog za GET /api/grants — UI koristi za slaganje GrantGroup-a.</summary>
    public static readonly IReadOnlyList<GrantDefinition> Catalog = new List<GrantDefinition>
    {
        new(EmployeesDirectoryView, "employees", "Kolegijalni pogled na zaposlenike (bez osjetljivih polja)."),
        new(EmployeesView, "employees", "Puni pregled zaposlenika (OIB, plaća, adresa...)."),
        new(EmployeesManage, "employees", "Kreiranje, uređivanje, aktivacija/deaktivacija, brisanje zaposlenika."),
        new(EmployeesRoleManage, "employees", "Promjena role (grant-grupe) drugog korisnika — eskalacijska ovlast."),
        new(EmployeesEngagementTypesView, "employees", "Pregled šifrarnika vrsta angažmana."),
        new(EmployeesEngagementTypesManage, "employees", "Uređivanje šifrarnika vrsta angažmana."),

        new(CatalogCompaniesView, "catalog", "Pregled tvrtki/lokacija."),
        new(CatalogCompaniesManage, "catalog", "Uređivanje tvrtki/lokacija."),
        new(CatalogServicesView, "catalog", "Pregled usluga."),
        new(CatalogServicesManage, "catalog", "Uređivanje usluga."),
        new(CatalogPackagesView, "catalog", "Pregled paketa."),
        new(CatalogPackagesManage, "catalog", "Uređivanje paketa."),
        new(CatalogPriceListView, "catalog", "Pregled cjenika."),
        new(CatalogPriceListManage, "catalog", "Uređivanje cjenika."),

        new(ClientsView, "clients", "Pregled klijenata (potpuno transparentno, bez own/all podjele)."),
        new(ClientsManage, "clients", "Kreiranje i uređivanje klijenata."),
        new(ClientsStatusManage, "clients", "Aktivacija/deaktivacija/brisanje klijenta."),
        new(ClientsAnonymize, "clients", "GDPR anonimizacija klijenta — nepovratno, najosjetljivije."),
        new(ClientsTagsView, "clients", "Pregled oznaka klijenata."),
        new(ClientsTagsManage, "clients", "Uređivanje oznaka klijenata."),
        new(ClientsPackagesView, "clients", "Pregled paketa klijenta."),
        new(ClientsPackagesManage, "clients", "Dodjela paketa klijentu."),

        new(AppointmentsView, "appointments", "Pregled rasporeda termina (transparentno)."),
        new(AppointmentsWriteOwn, "appointments", "Zakazivanje/uređivanje/otkazivanje vlastitih termina."),
        new(AppointmentsWriteAll, "appointments", "Zakazivanje/uređivanje/otkazivanje bilo čijih termina."),
        new(AppointmentsDelete, "appointments", "Trajno brisanje termina (isti dan)."),

        new(ScheduleBreaksView, "schedule-breaks", "Pregled pauza na rasporedu (transparentno, kao raspored termina)."),
        new(ScheduleBreaksWriteOwn, "schedule-breaks", "Kreiranje/uređivanje/brisanje vlastitih pauza."),
        new(ScheduleBreaksWriteAll, "schedule-breaks", "Kreiranje/uređivanje/brisanje bilo čijih pauza."),

        new(GroupsView, "groups", "Pregled grupa i članstava (transparentno)."),
        new(GroupsManage, "groups", "Kreiranje/uređivanje grupa, slotova, članova, generiranje termina."),
        new(GroupsAttendanceView, "groups", "Pregled prisutnosti na grupnim terminima."),
        new(GroupsAttendanceOwn, "groups", "Čekiranje prisutnosti na vlastitim grupnim terminima."),
        new(GroupsAttendanceAll, "groups", "Čekiranje prisutnosti na bilo čijim grupnim terminima."),

        new(RosterTypesView, "roster", "Pregled šifrarnika vrsta rostera."),
        new(RosterTypesManage, "roster", "Uređivanje šifrarnika vrsta rostera."),
        new(RosterEntriesView, "roster", "Pregled zapisa rostera (transparentno, svi vide sve)."),
        new(RosterEntriesWriteOwn, "roster", "Uređivanje vlastitih zapisa rostera."),
        new(RosterEntriesWriteAll, "roster", "Uređivanje bilo čijih zapisa rostera."),
        new(RosterReviewsTeamView, "roster", "Timski mjesečni pregled rostera (transparentno)."),
        new(RosterReviewsPersonalViewOwn, "roster", "Osobni pregled rostera — samo vlastiti."),
        new(RosterReviewsPersonalViewAll, "roster", "Osobni pregled rostera — bilo čiji."),
        new(RosterTemplatesView, "roster", "Pregled predložaka radnog vremena (zaposlenik/lokacija)."),
        new(RosterTemplatesManage, "roster", "Uređivanje predložaka radnog vremena — generira tvrdu blokadu zakazivanja."),
        new(RosterLeaveFundSettingsView, "roster", "Pregled postavki fonda godišnjeg odmora po zaposleniku."),
        new(RosterLeaveFundSettingsManage, "roster", "Uređivanje postavki fonda godišnjeg odmora (broj dana, datum obnove/isteka prijenosa)."),
        new(RosterLeaveFundViewOwn, "roster", "Pregled fonda godišnjeg odmora — samo vlastiti."),
        new(RosterLeaveFundViewAll, "roster", "Pregled fonda godišnjeg odmora — bilo čiji."),
        new(RosterLeaveFundManage, "roster", "Ručno otvaranje/korekcija fonda godišnjeg odmora za određenu godinu."),
    };
}

/// <summary>Jedna stavka kataloga grantova — izlaže se preko GET /api/grants radi slaganja GrantGroup-a u UI.</summary>
public record GrantDefinition(string Key, string Module, string Description);