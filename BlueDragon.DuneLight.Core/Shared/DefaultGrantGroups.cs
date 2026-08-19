using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Kanonski popis tri default GrantGroup-e (Admin/Trener/Recepcija) koje svaka organizacija treba imati odmah
/// nakon nastanka. Jedini izvor istine za AuthService.Register (nove organizacije). Migration_2026_08_GrantSystem
/// je povijesni, već izvršeni SQL koji je iste grupe retroaktivno stvorio za stare organizacije — DatabaseMigration
/// projekt nema referencu na Core pa migracija ne može pozivati ovu klasu izravno, samo je ručno drži usklađenom
/// (vidi popratnu migraciju koja postojeće "Recepcija" grupe dopunjuje istim grantovima kao ovdje).
/// </summary>
public static class DefaultGrantGroups
{
    public const string AdminGroupName = "Admin";
    public const string TrainerGroupName = "Trener";
    public const string ReceptionGroupName = "Recepcija";

    /// <summary>Admin vidi/upravlja svime — cijeli katalog grantova.</summary>
    public static readonly IReadOnlyList<string> AdminGrants = Grants.Catalog.Select(g => g.Key).ToList();

    public static readonly IReadOnlyList<string> TrainerGrants = new List<string>
    {
        Grants.EmployeesDirectoryView,
        Grants.CatalogCompaniesView, Grants.CatalogServiceCategoriesView, Grants.CatalogServicesView,
        Grants.CatalogPackagesView, Grants.CatalogPriceListView,
        Grants.ClientsView, Grants.ClientsManage, Grants.ClientsTagsView, Grants.ClientsPackagesView, Grants.ClientsPackagesManage,
        Grants.AppointmentsView, Grants.AppointmentsWriteOwn,
        Grants.GroupsView, Grants.GroupsAttendanceView, Grants.GroupsAttendanceOwn,
        Grants.RosterTypesView, Grants.RosterEntriesView, Grants.RosterEntriesWriteOwn,
        Grants.RosterReviewsTeamView, Grants.RosterReviewsPersonalViewOwn,
        Grants.RosterLeaveFundViewOwn
    };

    /// <summary>Recepcija radi raspored ZA SVE trenere (appointments.write.all, ne .own — nema vlastiti raspored)
    /// i vidi tko je odsutan/slobodan (roster.entries.view) radi zakazivanja, ali ne upisuje svoj roster.</summary>
    public static readonly IReadOnlyList<string> ReceptionGrants = new List<string>
    {
        Grants.EmployeesDirectoryView,
        Grants.ClientsView, Grants.ClientsManage, Grants.ClientsTagsView, Grants.ClientsPackagesView, Grants.ClientsPackagesManage,
        Grants.AppointmentsView, Grants.AppointmentsWriteAll,
        Grants.RosterEntriesView
    };
}