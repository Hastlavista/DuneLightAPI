using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za default tenant GrantGroup predloške (Admin/Trener/Recepcija) koje AuthService.Register
/// kreira za SVAKU novu organizaciju (vidi IGrantGroupHandler.EnsureDefaultGrantGroups). Ovo su i dalje raw-grant
/// predlošci (FAZA 1, prije CapabilityDefinition) — ne postoji zaseban stupac za "template key" u grant_groups
/// tablici, pa je <see cref="DefaultGrantGroupDefinition.Key"/> samo interni programski identifikator; jedina
/// veza prema bazi je <see cref="DefaultGrantGroupDefinition.DisplayName"/> (grant_groups.name), što je i granica
/// ove faze — vidi DefaultGrantGroupDriftChecker za posljedice (name-based matching, ne stabilni ID).
///
/// Izvedeno iz SeedDefaultGrantGroupsAndBackfillOwner (2026-08-17) + svih naknadnih backfill migracija koje su
/// eksplicitno spominjale "Admin"/"Trener"/"Recepcija" (ExpandDefaultReceptionGrants, BackfillAdminBrandingGrant,
/// ExpandAdminGrantsWithWorkingHoursTemplates, ExpandGrantsWithLeaveFund, ExpandAdminGrantsWithRooms) — NE iz
/// same originalne migracije, jer ta migracija više ne odražava trenutno ponašanje proizvoda.
///
/// Admin je namjerno == cijeli Grants.Catalog (dinamički, ne statička kopija) — to je isti obrazac koji su
/// komentari backfill migracija već najavljivali ("nove organizacije dobivaju grant automatski jer
/// AuthService.Register čita DefaultGrantGroups.AdminGrants, izveden iz cijelog Grants.Catalog"). Posljedica:
/// Admin uključuje i visoko-rizične grantove (clients.anonymize, appointments.delete, employees.role.manage) —
/// to NIJE nagađanje, nego doslovno ponašanje originalne seed migracije (all_grants ARRAY ih je već sadržavao).
///
/// Trener/Recepcija su NAMJERNO ostali kod svog izvornog opsega (own/samoposluživanje za Trenera, recepcijske/
/// poslovne operacije za Recepciju) — moduli dodani nakon 2026-08-17 (catalog.rooms.*, schedule-breaks.*,
/// roster.templates.*, roster.leave-fund.settings.*/.manage, checkout.*, products.*, stock.*, commissions.*,
/// dashboard.view, notifications.view, organization.*, employees.engagement-types.*) NIKAD nisu eksplicitno
/// dodijeljeni ovim dvjema grupama ni u jednoj backfill migraciji, pa se njihovo isključenje ovdje svjesno
/// zadržava umjesto izmišljanja novog opsega (vidi FAZA 1 Part B — "flag ambiguous grant, don't invent intent").
/// Ako se to treba promijeniti, to je proizvodna odluka za buduću fazu, ne za ovaj popravak lifecycle-a.
/// </summary>
public static class DefaultGrantGroups
{
    public const string AdminKey = "admin";
    public const string TrenerKey = "trener";
    public const string RecepcijaKey = "recepcija";

    public const string AdminDisplayName = "Admin";
    public const string TrenerDisplayName = "Trener";
    public const string RecepcijaDisplayName = "Recepcija";

    /// <summary>Admin = cijeli Grants.Catalog, uvijek — vidi obrazloženje u XML dokumentaciji klase.</summary>
    public static readonly IReadOnlyList<string> AdminGrants = Grants.Catalog
        .Select(g => g.Key)
        .ToList();

    /// <summary>Vlastiti/operativni opseg trenera — vidi trainer_grants u SeedDefaultGrantGroupsAndBackfillOwner
    /// + roster.leave-fund.view.own iz ExpandGrantsWithLeaveFund. catalog.service-categories.view je izostavljen
    /// jer je taj grant/modul otad uklonjen (Migration_2026_08_21_RemoveServiceCategory).</summary>
    public static readonly IReadOnlyList<string> TrenerGrants = new List<string>
    {
        Grants.EmployeesDirectoryView,
        Grants.CatalogCompaniesView,
        Grants.CatalogServicesView,
        Grants.CatalogPackagesView,
        Grants.CatalogPriceListView,
        Grants.ClientsView,
        Grants.ClientsManage,
        Grants.ClientsTagsView,
        Grants.ClientsPackagesView,
        Grants.ClientsPackagesManage,
        Grants.AppointmentsView,
        Grants.AppointmentsWriteOwn,
        Grants.GroupsView,
        Grants.GroupsAttendanceView,
        Grants.GroupsAttendanceOwn,
        Grants.RosterTypesView,
        Grants.RosterEntriesView,
        Grants.RosterEntriesWriteOwn,
        Grants.RosterReviewsTeamView,
        Grants.RosterReviewsPersonalViewOwn,
        Grants.RosterLeaveFundViewOwn,
    };

    /// <summary>Recepcijski/poslovni opseg — vidi reception_grants u SeedDefaultGrantGroupsAndBackfillOwner +
    /// appointments.view/appointments.write.all/roster.entries.view iz ExpandDefaultReceptionGrants (recepcija
    /// zakazuje za sve trenere i vidi tko je slobodan, ali ne upisuje svoj roster).</summary>
    public static readonly IReadOnlyList<string> RecepcijaGrants = new List<string>
    {
        Grants.EmployeesDirectoryView,
        Grants.ClientsView,
        Grants.ClientsManage,
        Grants.ClientsTagsView,
        Grants.ClientsPackagesView,
        Grants.ClientsPackagesManage,
        Grants.AppointmentsView,
        Grants.AppointmentsWriteAll,
        Grants.RosterEntriesView,
    };

    public static readonly DefaultGrantGroupDefinition Admin = new(AdminKey, AdminDisplayName, AdminGrants);
    public static readonly DefaultGrantGroupDefinition Trener = new(TrenerKey, TrenerDisplayName, TrenerGrants);
    public static readonly DefaultGrantGroupDefinition Recepcija = new(RecepcijaKey, RecepcijaDisplayName, RecepcijaGrants);

    /// <summary>Redoslijed kreiranja za nove organizacije — vidi IGrantGroupHandler.EnsureDefaultGrantGroups.</summary>
    public static readonly IReadOnlyList<DefaultGrantGroupDefinition> All = new List<DefaultGrantGroupDefinition>
    {
        Admin,
        Trener,
        Recepcija,
    };
}

/// <summary>Jedan default GrantGroup predložak. Key je interni programski identifikator (nije spremljen u bazu —
/// grant_groups nema stupac za njega u ovoj fazi); DisplayName je jedina poveznica prema stvarnom retku u bazi
/// (grant_groups.name), pa je i jedina osnova za prepoznavanje predloška kod postojećih grupa (vidi
/// DefaultGrantGroupDriftChecker).</summary>
public record DefaultGrantGroupDefinition(string Key, string DisplayName, IReadOnlyList<string> Grants);
