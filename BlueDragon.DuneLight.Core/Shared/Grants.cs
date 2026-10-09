using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za sve grant-ključeve u sustavu. Grantovi su definirani U KODU (ne u bazi) —
/// GrantGroup u bazi samo bira podskup ovih ključeva. Ne mijenjati postojeće vrijednosti (GrantGroup
/// zapisi u bazi ih referenciraju kao stringove), samo dodavati nove. Novi grant zahtijeva i migraciju koja ga
/// dodaje inicijalnim Admin grupama (grant_groups.system_key = 'admin', vidi ADR-0023).
/// </summary>
public static class Grants
{
    public const string EmployeesDirectoryView = "employees.directory.view";
    public const string EmployeesView = "employees.view";
    public const string EmployeesManage = "employees.manage";
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
    public const string CatalogRoomsView = "catalog.rooms.view";
    public const string CatalogRoomsManage = "catalog.rooms.manage";
    public const string CatalogResourcesView = "catalog.resources.view";
    public const string CatalogResourcesManage = "catalog.resources.manage";

    /// <summary>K1-4 — upravljanje šifrarnikom razloga otkazivanja/izostanka (pregled šifri imaju svi koji otkazuju).</summary>
    public const string CatalogCancellationReasonsManage = "catalog.cancellation-reasons.manage";
    public const string CatalogCancellationPoliciesView = "catalog.cancellation-policies.view";
    public const string CatalogCancellationPoliciesManage = "catalog.cancellation-policies.manage";
    public const string CatalogMembershipsView = "catalog.memberships.view";
    public const string CatalogMembershipsManage = "catalog.memberships.manage";
    public const string CatalogMembershipsDeactivate = "catalog.memberships.deactivate";

    public const string ClientsView = "clients.view";
    public const string ClientsManage = "clients.manage";
    public const string ClientsStatusManage = "clients.status.manage";
    public const string ClientsAnonymize = "clients.anonymize";
    public const string ClientsTagsView = "clients.tags.view";
    public const string ClientsTagsManage = "clients.tags.manage";
    public const string ClientsPackagesView = "clients.packages.view";
    public const string ClientsPackagesManage = "clients.packages.manage";
    public const string ClientsMembershipsView = "clients.memberships.view";
    public const string ClientsMembershipsSell = "clients.memberships.sell";
    public const string ClientsMembershipsCancel = "clients.memberships.cancel";
    public const string ClientsMembershipsPause = "clients.memberships.pause";
    public const string ClientsMembershipsPlanChange = "clients.memberships.plan-change";
    public const string ClientsMembershipsEndOverride = "clients.memberships.end-override";
    public const string ClientsMembershipsVoidSale = "clients.memberships.void-sale";
    public const string MembershipsChargesWriteOff = "memberships.charges.write-off";

    public const string AppointmentsView = "appointments.view";
    public const string AppointmentsWriteOwn = "appointments.write.own";
    public const string AppointmentsWriteAll = "appointments.write.all";
    public const string AppointmentsDelete = "appointments.delete";
    /// <summary>K2 (12.2, ADR-0032) — otpis posljedice politike otkazivanja koja naplaćuje NAKNADU (u trenutku događaja ili
    /// naknadno; razlog obavezan). S AppointmentsPolicyUnitWaive zamjenjuje ugašeni appointments.policy.override. Nikad ne
    /// širi own opseg.</summary>
    public const string AppointmentsPolicyFeeWaive = "appointments.policy.fee.waive";
    /// <summary>K2 (12.2, ADR-0032) — otpis posljedice koja troši JEDINICU paketa ili kredit članarine. Nikad ne širi own opseg.</summary>
    public const string AppointmentsPolicyUnitWaive = "appointments.policy.unit.waive";

    /// <summary>K2 (14.2, ADR-0032) — korekcija statusa sudjelovanja ZATVORENOG termina po izvornom statusu, ponovno otvaranje
    /// termina s tim statusom i (Cancelled) "vrati termin". Prije zatvaranja promjena statusa ne traži grant. Nikad ne širi
    /// own opseg.</summary>
    public const string AppointmentsCorrectionsCompleted = "appointments.corrections.completed";
    public const string AppointmentsCorrectionsNoShow = "appointments.corrections.no-show";
    public const string AppointmentsCorrectionsCancelled = "appointments.corrections.cancelled";

    /// <summary>K2 (P-2, ADR-0032) — rad izvan radnog vremena / dostupnosti (odsutnost, pauza, praznik) uz OverrideAvailability:
    /// kreiranje, niz, izmjena segmenta, "Upiši odrađeno", "vrati termin" i generiranje grupa. Neovisan o opsegu (own +
    /// override = samo vlastiti termin).</summary>
    public const string AppointmentsAvailabilityOverride = "appointments.availability.override";
    /// <summary>P2 (Q54) — rezervacija člana u dugu unatoč postavci "blokiraj rezervaciju" (Q15.4/Q18.3); takvo sudjelovanje je
    /// bez pokrića.</summary>
    public const string AppointmentsMembershipBlockOverride = "appointments.membership-block.override";

    /// <summary>K1-2 — označavanje i poništavanje dolaska klijenta na bilo kojem terminu, bez prava uređivanja termina.</summary>
    public const string AppointmentsArrivalMark = "appointments.arrival.mark";

    public const string ScheduleBreaksView = "schedule.breaks.view";
    public const string ScheduleBreaksWriteOwn = "schedule.breaks.write.own";
    public const string ScheduleBreaksWriteAll = "schedule.breaks.write.all";

    public const string GroupsView = "groups.view";
    public const string GroupsManage = "groups.manage";
    /// <summary>Phase M1F — eksplicitno prekoračenje MEKOG kapaciteta segmenta grupe (zahtjev mora tražiti override).</summary>
    public const string GroupsCapacityOverride = "groups.capacity.override";
    public const string GroupsAttendanceView = "groups.attendance.view";
    public const string GroupsAttendanceOwn = "groups.attendance.own";
    public const string GroupsAttendanceAll = "groups.attendance.all";

    public const string RosterTypesView = "roster.types.view";
    public const string RosterTypesManage = "roster.types.manage";
    public const string RosterEntriesView = "roster.entries.view";
    public const string RosterEntriesWriteOwn = "roster.entries.write.own";
    public const string RosterEntriesWriteAll = "roster.entries.write.all";
    /// <summary>K2 (P-4, ADR-0032) — upis, izmjena i brisanje roster zapisa koji počinje prije današnjeg dana organizacije (stari
    /// ili novi datum). Uz own/all opseg, nikad ga ne širi; bez vremenske granice (ARCH §7.3).</summary>
    public const string RosterEntriesWritePast = "roster.entries.write.past";
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

    public const string OrganizationBrandingManage = "organization.branding.manage";
    public const string OrganizationSettingsManage = "organization.settings.manage";

    /// <summary>Grant-only Tenant Authorization Refactor — zamjenjuju stari [RequireOwner] Owner bypass na
    /// GrantGroups/Grants/Capabilities/Roles/GrantDiagnostics kontrolerima. Nema veze s GrantGroup imenom ni
    /// legacy UserRole — bilo koja GrantGroup s ovim ključevima smije upravljati dozvolama.</summary>
    public const string PermissionsView = "permissions.view";
    public const string PermissionsManage = "permissions.manage";
    public const string PermissionsAssignmentsManage = "permissions.assignments.manage";

    /// <summary>Namjerno bez own/all podjele — POS/checkout je vezan uz poslovnicu (Company), ne uz "vlastite"
    /// termine pojedinog trenera (vidi spec section 78).</summary>
    public const string CheckoutView = "checkout.view";
    public const string CheckoutManage = "checkout.manage";

    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";
    public const string StockView = "stock.view";
    public const string StockManage = "stock.manage";

    /// <summary>Namjerno bez own/all podjele — provizija je internа staff-compensation evidencija koju vidi
    /// menadžment, ne "vlastita" provizija pojedinog zaposlenika (vidi spec section 47). View pokriva samo
    /// čitanje CommissionEntry povijesti/sažetka; konfiguracija pravila (uklj. čitanje pravila) je Manage.</summary>
    public const string CommissionsView = "commissions.view";
    public const string CommissionsManage = "commissions.manage";

    /// <summary>Namjerno bez own/all podjele — operativna nadzorna ploča je vezana uz poslovnicu (Company), isto
    /// obrazloženje kao CheckoutView. Read-only (nema Manage parnjaka) jer dashboard ništa ne mutira.</summary>
    public const string DashboardView = "dashboard.view";

    /// <summary>Namjerno bez own/all podjele i bez Manage parnjaka — Notification je interna/operativna povijest
    /// (vidi spec section 30/58), read-only, ništa se ne konfigurira kroz API.</summary>
    public const string NotificationsView = "notifications.view";

    /// <summary>Puni katalog za GET /api/grants — UI koristi za slaganje GrantGroup-a.</summary>
    public static readonly IReadOnlyList<GrantDefinition> Catalog = new List<GrantDefinition>
    {
        new(EmployeesDirectoryView, "Imenik zaposlenika", "employees", "Kolegijalni pogled na zaposlenike (bez osjetljivih polja)."),
        new(EmployeesView, "Pregled zaposlenika", "employees", "Puni pregled zaposlenika (OIB, plaća, adresa...)."),
        new(EmployeesManage, "Upravljanje zaposlenicima", "employees", "Kreiranje, uređivanje, aktivacija/deaktivacija, brisanje zaposlenika."),
        new(EmployeesEngagementTypesView, "Pregled vrsta angažmana", "employees", "Pregled šifrarnika vrsta angažmana."),
        new(EmployeesEngagementTypesManage, "Upravljanje vrstama angažmana", "employees", "Uređivanje šifrarnika vrsta angažmana."),

        new(CatalogCompaniesView, "Pregled poslovnica", "catalog", "Pregled tvrtki."),
        new(CatalogCompaniesManage, "Upravljanje poslovnicama", "catalog", "Uređivanje tvrtki."),
        new(CatalogServicesView, "Pregled usluga", "catalog", "Pregled usluga."),
        new(CatalogServicesManage, "Upravljanje uslugama", "catalog", "Uređivanje usluga."),
        new(CatalogPackagesView, "Pregled paketa", "catalog", "Pregled paketa."),
        new(CatalogPackagesManage, "Upravljanje paketima", "catalog", "Uređivanje paketa."),
        new(CatalogPriceListView, "Pregled cjenika", "catalog", "Pregled cjenika."),
        new(CatalogPriceListManage, "Upravljanje cjenikom", "catalog", "Uređivanje cjenika."),
        new(CatalogRoomsView, "Pregled prostorija", "catalog", "Pregled prostorija po poslovnici."),
        new(CatalogRoomsManage, "Upravljanje prostorijama", "catalog", "Uređivanje prostorija po poslovnici."),
        new(CatalogResourcesView, "Pregled resursa", "catalog", "Pregled resursa (oprema/mjesta s kapacitetom) po poslovnici."),
        new(CatalogResourcesManage, "Upravljanje resursima", "catalog", "Uređivanje resursa (oprema/mjesta s kapacitetom) po poslovnici."),
        new(CatalogCancellationReasonsManage, "Upravljanje razlozima otkazivanja", "catalog", "Šifrarnik razloga otkazivanja i izostanka (naziv, aktivnost, za koje događaje vrijedi)."),
        new(CatalogCancellationPoliciesView, "Pregled politika otkazivanja", "catalog", "Pregled politika otkazivanja, njihovih verzija i dodjela."),
        new(CatalogCancellationPoliciesManage, "Upravljanje politikama otkazivanja", "catalog", "Kreiranje politika otkazivanja i verzija, dodjele po poslovnici/usluzi i zadana politika organizacije."),
        new(CatalogMembershipsView, "Pregled planova članarina", "catalog", "Pregled planova članarina i njihovih verzija uvjeta."),
        new(CatalogMembershipsManage, "Upravljanje planovima članarina", "catalog", "Kreiranje i uređivanje planova članarina, objava novih verzija uvjeta (i prijenos na postojeća članstva uz najavu) te kapacitet prodaje."),
        new(CatalogMembershipsDeactivate, "Aktivacija i deaktivacija planova članarina", "catalog", "Deaktivacija plana zaustavlja prodaju i obnovu svih njegovih članstava; aktivacija je vraća."),

        new(ClientsView, "Pregled klijenata", "clients", "Pregled klijenata (potpuno transparentno, bez own/all podjele)."),
        new(ClientsManage, "Upravljanje klijentima", "clients", "Kreiranje i uređivanje klijenata."),
        new(ClientsStatusManage, "Status klijenta", "clients", "Aktivacija/deaktivacija/brisanje klijenta."),
        new(ClientsAnonymize, "Anonimizacija klijenta", "clients", "GDPR anonimizacija klijenta — nepovratno, najosjetljivije."),
        new(ClientsTagsView, "Pregled oznaka klijenata", "clients", "Pregled oznaka klijenata."),
        new(ClientsTagsManage, "Upravljanje oznakama klijenata", "clients", "Uređivanje oznaka klijenata."),
        new(ClientsPackagesView, "Pregled paketa klijenata", "clients", "Pregled paketa klijenta."),
        new(ClientsPackagesManage, "Dodjela paketa klijentima", "clients", "Dodjela paketa klijentu."),
        new(ClientsMembershipsView, "Pregled članstava", "clients", "Pregled članarina klijenata, njihovog stanja, pauza i zakazanih promjena."),
        new(ClientsMembershipsSell, "Prodaja članarina", "clients", "Prodaja članarine klijentu (plan, datum početka, poslovnica prodaje)."),
        new(ClientsMembershipsCancel, "Otkaz članarine", "clients", "Zahtjev za otkaz članarine (djeluje prema otkaznom roku i minimalnoj obvezi) i povlačenje zakazanog otkaza."),
        new(ClientsMembershipsPause, "Pauza članarine", "clients", "Zadavanje pauze, raniji povratak iz pauze i otkaz pauze koja još nije počela."),
        new(ClientsMembershipsPlanChange, "Promjena plana članarine", "clients", "Promjena plana od sljedećeg ciklusa i povlačenje zakazane promjene."),
        new(ClientsMembershipsEndOverride, "Raniji izlazak iz članarine", "clients", "Ručno nadjačavanje datuma završetka (raniji izlazak bez penala, uz razlog)."),
        new(ClientsMembershipsVoidSale, "Poništavanje prodaje članarine", "clients", "Poništavanje prodaje članarine koja nije plaćena ni korištena."),
        new(MembershipsChargesWriteOff, "Otpis zaduženja članarine", "clients", "Otpis preostalog duga zaduženja članarine (uz razlog); otpisano zaduženje je konačno."),

        new(AppointmentsView, "Pregled termina", "appointments", "Pregled rasporeda termina (transparentno)."),
        new(AppointmentsWriteOwn, "Vlastiti termini", "appointments", "Zakazivanje/uređivanje/otkazivanje vlastitih termina."),
        new(AppointmentsWriteAll, "Svi termini", "appointments", "Zakazivanje/uređivanje/otkazivanje bilo čijih termina."),
        new(AppointmentsDelete, "Brisanje termina", "appointments", "Trajno brisanje termina (isti dan)."),
        new(AppointmentsPolicyFeeWaive, "Otpis naknade politike otkazivanja", "appointments", "Otpis naknade kasnog otkazivanja ili izostanka (uz razlog), u trenutku događaja ili naknadno."),
        new(AppointmentsPolicyUnitWaive, "Otpis jedinice ili kredita politike otkazivanja", "appointments", "Otpis jedinice paketa ili kredita članarine skinutog zbog kasnog otkazivanja ili izostanka (uz razlog); jedinica se vraća."),
        new(AppointmentsCorrectionsCompleted, "Korekcija odrađenog", "appointments", "Promjena statusa odrađenog sudjelovanja na zatvorenom terminu (uz razlog) i ponovno otvaranje takvog termina."),
        new(AppointmentsCorrectionsNoShow, "Korekcija izostanka", "appointments", "Promjena statusa izostanka na zatvorenom terminu (uz razlog) i ponovno otvaranje takvog termina."),
        new(AppointmentsCorrectionsCancelled, "Korekcija otkazanog", "appointments", "Promjena statusa otkazanog sudjelovanja na zatvorenom terminu (uz razlog), ponovno otvaranje takvog termina i vraćanje otkazanog termina."),
        new(AppointmentsAvailabilityOverride, "Zakazivanje izvan radnog vremena", "appointments", "Zakazivanje i izmjena termina izvan radnog vremena, uz odsutnost, pauzu ili praznik (uz potvrdu), uključujući generiranje grupa."),
        new(AppointmentsMembershipBlockOverride, "Rezervacija unatoč blokadi duga članarine", "appointments", "Rezervacija člana čija je članarina u dugu uz postavku \"blokiraj rezervaciju\"; sesija je bez pokrića."),
        new(AppointmentsArrivalMark, "Označavanje dolaska", "appointments", "Označavanje i poništavanje dolaska klijenta na terminu (bez prava uređivanja termina; bez financijskog učinka)."),

        new(ScheduleBreaksView, "Pregled pauza", "schedule-breaks", "Pregled pauza na rasporedu (transparentno, kao raspored termina)."),
        new(ScheduleBreaksWriteOwn, "Vlastite pauze", "schedule-breaks", "Kreiranje/uređivanje/brisanje vlastitih pauza."),
        new(ScheduleBreaksWriteAll, "Sve pauze", "schedule-breaks", "Kreiranje/uređivanje/brisanje bilo čijih pauza."),

        new(GroupsView, "Pregled grupa", "groups", "Pregled grupa i članstava (transparentno)."),
        new(GroupsManage, "Upravljanje grupama", "groups", "Kreiranje/uređivanje grupa, slotova, članova, generiranje termina."),
        new(GroupsCapacityOverride, "Prekoračenje kapaciteta grupe", "groups", "Eksplicitno prekoračenje poslovnog kapaciteta segmenta grupe (fizički kapacitet prostorije/resursa i dalje vrijedi)."),
        new(GroupsAttendanceView, "Pregled prisutnosti", "groups", "Pregled prisutnosti na grupnim terminima."),
        new(GroupsAttendanceOwn, "Prisutnost na vlastitim grupnim terminima", "groups", "Čekiranje prisutnosti na vlastitim grupnim terminima."),
        new(GroupsAttendanceAll, "Prisutnost na svim grupnim terminima", "groups", "Čekiranje prisutnosti na bilo čijim grupnim terminima."),

        new(RosterTypesView, "Pregled vrsta rostera", "roster", "Pregled šifrarnika vrsta rostera."),
        new(RosterTypesManage, "Upravljanje vrstama rostera", "roster", "Uređivanje šifrarnika vrsta rostera."),
        new(RosterEntriesView, "Pregled rostera", "roster", "Pregled zapisa rostera (transparentno, svi vide sve)."),
        new(RosterEntriesWriteOwn, "Vlastiti roster", "roster", "Uređivanje vlastitih zapisa rostera."),
        new(RosterEntriesWriteAll, "Roster svih zaposlenika", "roster", "Uređivanje bilo čijih zapisa rostera."),
        new(RosterEntriesWritePast, "Roster u prošlosti", "roster", "Upis, izmjena i brisanje roster zapisa koji počinje prije današnjeg dana (uz vlastiti ili opseg svih)."),
        new(RosterReviewsTeamView, "Timski pregled rostera", "roster", "Timski mjesečni pregled rostera (transparentno)."),
        new(RosterReviewsPersonalViewOwn, "Osobni pregled rostera (vlastiti)", "roster", "Osobni pregled rostera — samo vlastiti."),
        new(RosterReviewsPersonalViewAll, "Osobni pregled rostera (svi)", "roster", "Osobni pregled rostera — bilo čiji."),
        new(RosterTemplatesView, "Pregled predložaka radnog vremena", "roster", "Pregled predložaka radnog vremena (zaposlenik/poslovnica)."),
        new(RosterTemplatesManage, "Upravljanje predlošcima radnog vremena", "roster", "Uređivanje predložaka radnog vremena — generira tvrdu blokadu zakazivanja."),
        new(RosterLeaveFundSettingsView, "Pregled postavki fonda godišnjeg odmora", "roster", "Pregled postavki fonda godišnjeg odmora po zaposleniku."),
        new(RosterLeaveFundSettingsManage, "Postavke fonda godišnjeg odmora", "roster", "Uređivanje postavki fonda godišnjeg odmora (broj dana, datum obnove/isteka prijenosa)."),
        new(RosterLeaveFundViewOwn, "Vlastiti fond godišnjeg odmora", "roster", "Pregled fonda godišnjeg odmora — samo vlastiti."),
        new(RosterLeaveFundViewAll, "Fond godišnjeg odmora svih zaposlenika", "roster", "Pregled fonda godišnjeg odmora — bilo čiji."),
        new(RosterLeaveFundManage, "Korekcija fonda godišnjeg odmora", "roster", "Ručno otvaranje/korekcija fonda godišnjeg odmora za određenu godinu."),

        new(OrganizationBrandingManage, "Vizualni identitet", "organization", "Uređivanje vizualnog identiteta organizacije (logo, favicon, boje)."),
        new(OrganizationSettingsManage, "Postavke organizacije", "organization", "Uređivanje poslovnih postavki organizacije (npr. potrošnja paketa, vremenska zona)."),

        new(PermissionsView, "Pregled dozvola", "organization", "Pregled GrantGroup-a i konfiguracije dozvola."),
        new(PermissionsManage, "Upravljanje dozvolama", "organization", "Kreiranje/uređivanje/brisanje GrantGroup-a (izravno ili preko capability editora)."),
        new(PermissionsAssignmentsManage, "Dodjela grupa dozvola", "organization", "Dodjela GrantGroup-a korisnicima."),

        new(CheckoutView, "Pregled naplate", "checkout", "Pregled checkout/POS košarica i njihove povijesti plaćanja."),
        new(CheckoutManage, "Naplata", "checkout", "Kreiranje/uređivanje checkout košarica, naplata, poništenje plaćanja, dovršetak/otkazivanje."),

        new(ProductsView, "Pregled proizvoda", "products", "Pregled kataloga proizvoda."),
        new(ProductsManage, "Upravljanje proizvodima", "products", "Kreiranje, uređivanje, aktivacija/deaktivacija, brisanje proizvoda."),
        new(StockView, "Pregled zaliha", "products", "Pregled zaliha po poslovnici i povijesti kretanja zalihe."),
        new(StockManage, "Upravljanje zalihama", "products", "Ručna korekcija zalihe i transfer zalihe između poslovnica."),

        new(CommissionsView, "Pregled provizija", "commissions", "Pregled zarađene provizije osoblja (povijest i sažetak)."),
        new(CommissionsManage, "Pravila provizija", "commissions", "Konfiguracija pravila provizije po zaposleniku/predmetu."),

        new(DashboardView, "Nadzorna ploča", "dashboard", "Pregled operativne nadzorne ploče (raspored, osoblje, financije, upozorenja) po poslovnici."),

        new(NotificationsView, "Pregled obavijesti", "notifications", "Pregled povijesti logičkih obavijesti po klijentu (interno/operativno)."),
    };
}

/// <summary>Jedna stavka kataloga grantova (ključ, naziv za prikaz, modul, opis) — izlaže se preko GET /api/grants radi
/// slaganja GrantGroup-a u UI.</summary>
public record GrantDefinition(string Key, string DisplayName, string Module, string Description);