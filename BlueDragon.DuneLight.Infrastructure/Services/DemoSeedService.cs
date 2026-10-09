using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Permissions;
using BlueDragon.DuneLight.Core.DTOs.Roster;
using BlueDragon.DuneLight.Core.DTOs.TestTools;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Interfaces.Permissions;
using BlueDragon.DuneLight.Core.Interfaces.Roster;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.TestTools;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Time;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// T1-4 — PRIVREMENI testni alat, uklanja se prije go-livea (vidi <see cref="IDemoSeedService"/>).
/// Sve ide kroz postojeće aplikacijske servise (iste validacije kao iz aplikacije): prodaja članarina i paketa, naplata kroz
/// checkout, pauza, otkaz, zatvaranje poslovnice, odrađivanje sesija, prisutnost, pravila provizija. Izravno se čita samo odabir
/// aktera (<see cref="IDemoSeedHandler"/>). Seed nije jedna transakcija (svaki servis sprema svoje): pri prekidu napraviti novu
/// demo organizaciju. Sve poslovne vremenske oznake nastaju na satu ciljne organizacije (<see cref="OrganizationClockContext"/>).
/// "Puni demo" stanja koja traže prošlost (dug nakon grace perioda, završeno članstvo, članstvo koje stoji, potrošene sesije)
/// slaže prodajom "u prošlosti" i skokom sata kroz <see cref="ITestToolsService.AdvanceClock"/> (isti mehanizam kao alat za
/// pomak vremena: prolaz obnove dan po dan), nikad ručnim datumima.
/// </summary>
public class DemoSeedService : IDemoSeedService
{
    private const string EmailDomain = "demo.dunelight.test";
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Najkraći skok sata "Punog demoa": dulji od mjesečnog perioda + grace perioda (7 dana), da obnova stvori dug,
    /// završi otkazano članstvo i otvori stajanje zbog zatvorene poslovnice.</summary>
    private const int MinimumJumpDays = 45;

    /// <summary>Dan u tjednu na koji skok sata "Punog demoa" završava: tekući tjedan ima prošle dane (pon–sri, s prisutnošću), a
    /// sljedeći tjedan je u budućnosti.</summary>
    private const DayOfWeek JumpTargetDay = DayOfWeek.Thursday;

    /// <summary>K2 grantovi koje grupa "Recepcija" namjerno NEMA (korekcije, override radne snage, otpisi, roster u prošlosti;
    /// T1-9: i paket unatrag).</summary>
    private static readonly HashSet<string> K2Grants = new()
    {
        Grants.AppointmentsCorrectionsCompleted,
        Grants.AppointmentsCorrectionsNoShow,
        Grants.AppointmentsCorrectionsCancelled,
        Grants.AppointmentsAvailabilityOverride,
        Grants.AppointmentsPolicyFeeWaive,
        Grants.AppointmentsPolicyUnitWaive,
        Grants.RosterEntriesWritePast,
        Grants.ClientsPackagesWritePast
    };

    /// <summary>Dio ovlasti za trenera (vlastiti termini, pregled klijenata).</summary>
    private static readonly List<string> TrainerGrants = new()
    {
        Grants.AppointmentsView,
        Grants.AppointmentsWriteOwn,
        Grants.ClientsView,
        Grants.EmployeesDirectoryView,
        Grants.CatalogCompaniesView,
        Grants.CatalogServicesView,
        Grants.GroupsView,
        Grants.GroupsAttendanceView,
        Grants.GroupsAttendanceOwn,
        Grants.ScheduleBreaksView,
        Grants.ScheduleBreaksWriteOwn,
        Grants.RosterEntriesView,
        Grants.RosterEntriesWriteOwn,
        Grants.RosterReviewsPersonalViewOwn
    };

    private static readonly (string First, string Last)[] ClientNames =
    {
        ("Ana", "Kovač"), ("Ivan", "Horvat"), ("Petra", "Babić"), ("Luka", "Marić"), ("Maja", "Novak"),
        ("Josip", "Jurić"), ("Lucija", "Knežević"), ("Marko", "Vuković"), ("Katarina", "Pavlović"), ("Tomislav", "Božić"),
        ("Ivana", "Kovačević"), ("Nikola", "Blažević"), ("Sara", "Grgić"), ("Filip", "Perić"), ("Ema", "Radić"),
        ("Dario", "Šarić"), ("Lana", "Lovrić"), ("Mateo", "Tomić"), ("Nina", "Matić"), ("Karlo", "Vidović")
    };

    // Klijenti s članarinama i paketima (nisu članovi grupe, da dug ne preskače članove grupe).
    private const int ActiveMemberClient = 10;
    private const int AwaitingOrDebtMemberClient = 11;
    private const int PausedMemberClient = 12;
    private const int EndedMemberClient = 13;
    private const int StandingStillMemberClient = 14;
    private const int TenPackClient = 15;
    private const int ThreePackClient = 16;

    private readonly TimeProvider _timeProvider;
    private readonly BusinessTimeProvider _businessTimeProvider;
    private readonly IAuthService _authService;
    private readonly ITestToolOrganizationHandler _testToolOrganizationHandler;
    private readonly IDemoSeedHandler _demoSeedHandler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly ICompanyService _companyService;
    private readonly ICompanyHolidayService _companyHolidayService;
    private readonly IWorkingHoursTemplateService _workingHoursTemplateService;
    private readonly IRoomService _roomService;
    private readonly IResourceService _resourceService;
    private readonly IServiceCatalogService _serviceCatalogService;
    private readonly IServiceAvailabilityService _serviceAvailabilityService;
    private readonly IPricingService _pricingService;
    private readonly IPackageService _packageService;
    private readonly IMembershipPlanService _membershipPlanService;
    private readonly ICancellationPolicyService _cancellationPolicyService;
    private readonly ICancellationReasonService _cancellationReasonService;
    private readonly IEngagementTypeService _engagementTypeService;
    private readonly IGrantGroupService _grantGroupService;
    private readonly IEmployeeService _employeeService;
    private readonly IClientService _clientService;
    private readonly IGroupService _groupService;
    private readonly IGroupAttendanceService _groupAttendanceService;
    private readonly IAppointmentService _appointmentService;
    private readonly IBookingService _bookingService;
    private readonly ICheckoutService _checkoutService;
    private readonly IClientMembershipService _clientMembershipService;
    private readonly IClientPackageService _clientPackageService;
    private readonly ICommissionRuleService _commissionRuleService;
    private readonly ICommissionService _commissionService;
    private readonly ITestToolsService _testToolsService;

    public DemoSeedService(
        TimeProvider timeProvider,
        BusinessTimeProvider businessTimeProvider,
        IAuthService authService,
        ITestToolOrganizationHandler testToolOrganizationHandler,
        IDemoSeedHandler demoSeedHandler,
        IOrganizationCalendarService organizationCalendarService,
        ICompanyService companyService,
        ICompanyHolidayService companyHolidayService,
        IWorkingHoursTemplateService workingHoursTemplateService,
        IRoomService roomService,
        IResourceService resourceService,
        IServiceCatalogService serviceCatalogService,
        IServiceAvailabilityService serviceAvailabilityService,
        IPricingService pricingService,
        IPackageService packageService,
        IMembershipPlanService membershipPlanService,
        ICancellationPolicyService cancellationPolicyService,
        ICancellationReasonService cancellationReasonService,
        IEngagementTypeService engagementTypeService,
        IGrantGroupService grantGroupService,
        IEmployeeService employeeService,
        IClientService clientService,
        IGroupService groupService,
        IGroupAttendanceService groupAttendanceService,
        IAppointmentService appointmentService,
        IBookingService bookingService,
        ICheckoutService checkoutService,
        IClientMembershipService clientMembershipService,
        IClientPackageService clientPackageService,
        ICommissionRuleService commissionRuleService,
        ICommissionService commissionService,
        ITestToolsService testToolsService)
    {
        _timeProvider = timeProvider;
        _businessTimeProvider = businessTimeProvider;
        _authService = authService;
        _testToolOrganizationHandler = testToolOrganizationHandler;
        _demoSeedHandler = demoSeedHandler;
        _organizationCalendarService = organizationCalendarService;
        _companyService = companyService;
        _companyHolidayService = companyHolidayService;
        _workingHoursTemplateService = workingHoursTemplateService;
        _roomService = roomService;
        _resourceService = resourceService;
        _serviceCatalogService = serviceCatalogService;
        _serviceAvailabilityService = serviceAvailabilityService;
        _pricingService = pricingService;
        _packageService = packageService;
        _membershipPlanService = membershipPlanService;
        _cancellationPolicyService = cancellationPolicyService;
        _cancellationReasonService = cancellationReasonService;
        _engagementTypeService = engagementTypeService;
        _grantGroupService = grantGroupService;
        _employeeService = employeeService;
        _clientService = clientService;
        _groupService = groupService;
        _groupAttendanceService = groupAttendanceService;
        _appointmentService = appointmentService;
        _bookingService = bookingService;
        _checkoutService = checkoutService;
        _clientMembershipService = clientMembershipService;
        _clientPackageService = clientPackageService;
        _commissionRuleService = commissionRuleService;
        _commissionService = commissionService;
        _testToolsService = testToolsService;
    }

    /// <summary>Što seed radi: "Osnova", "Puni demo" (nova organizacija, smije pomaknuti sat) ili dopuna postojeće organizacije
    /// (puni sadržaj, ali samo stanja ostvariva bez protoka vremena — sat se nikad ne pomiče).</summary>
    private enum SeedMode
    {
        Basic,
        FullDemo,
        ExistingFill
    }

    /// <summary>Razina demo organizacije iz retka testnih alata; demo bez zapisane razine (stvoren prije razina) je "Puni demo".</summary>
    public static DemoSeedLevel LevelOf(TestToolOrganization row) =>
        Enum.TryParse(row?.DemoLevel, out DemoSeedLevel level) ? level : DemoSeedLevel.Full;

    public async Task<DemoOrganizationResultDto> CreateDemoOrganization(DemoSeedLevel level)
    {
        if (!Enum.IsDefined(level))
            throw new ValidationAppException(ErrorCodes.ValidationError, "Nepoznata razina demo organizacije.");

        string tag = NewTag();
        string password = NewPassword();

        // Registracija kroz postojeći tok (organizacija, prvi korisnik, Admin grupa sa svim grantovima, zadani tipovi rostera,
        // neutralna zadana politika otkazivanja) — seed je ne duplicira.
        AuthResponse registered = await _authService.Register(new RegisterRequest
        {
            OrganizationName = level == DemoSeedLevel.Basic ? $"Demo osnova #{tag}" : $"Demo studio #{tag}",
            Email = $"vlasnik.{tag}@{EmailDomain}",
            Password = password
        });
        Guid organizationId = registered.OrganizationId.GetValueOrDefault();
        Guid founderId = registered.UserId.GetValueOrDefault();

        await _testToolOrganizationHandler.MarkDemo(organizationId, level.ToString(), _businessTimeProvider.GetRealUtcNow());

        DemoOrganizationResultDto result = new DemoOrganizationResultDto
        {
            OrganizationId = organizationId,
            Name = registered.OrganizationName,
            Slug = registered.OrganizationSlug,
            Level = level,
            RunTag = tag
        };
        result.Users.Add(new DemoSeedUserDto
        {
            UserId = founderId,
            Email = registered.Email,
            Password = password,
            Description = "Osnivač (registracija) — Admin grupa, svi grantovi, bez profila zaposlenika"
        });

        Guid adminGrantGroupId = await _demoSeedHandler.FindSystemAdminGrantGroupId(organizationId)
            ?? throw new InvalidOperationException("Registracija nije stvorila Admin grupu ovlasti.");
        await Seed(organizationId, founderId, adminGrantGroupId, tag, result,
            level == DemoSeedLevel.Basic ? SeedMode.Basic : SeedMode.FullDemo);
        return result;
    }

    public async Task<DemoOrganizationResultDto> ResetDemoOrganization(Guid organizationId)
    {
        if (!await _testToolOrganizationHandler.OrganizationExists(organizationId))
            throw new NotFoundAppException("Organization", organizationId);

        TestToolOrganization row = await _testToolOrganizationHandler.Get(organizationId);
        if (row is not { IsDemo: true } || row.RetiredAt != null)
            throw new BusinessRuleException(ErrorCodes.TestToolsNotDemoOrganization,
                "Reset je moguć samo za demo organizaciju koja nije već zamijenjena resetom.");

        // Prvo nova organizacija iste razine, pa tek onda umirovljenje stare: neuspjeh seeda ne ostavlja bez demo organizacije.
        DemoOrganizationResultDto result = await CreateDemoOrganization(LevelOf(row));
        await _testToolOrganizationHandler.Retire(organizationId, _businessTimeProvider.GetRealUtcNow());
        result.RetiredOrganizationId = organizationId;
        return result;
    }

    public async Task<DemoSeedResultDto> SeedExistingOrganization(Guid organizationId)
    {
        if (!await _testToolOrganizationHandler.OrganizationExists(organizationId))
            throw new NotFoundAppException("Organization", organizationId);

        Guid actorUserId = await _demoSeedHandler.FindActiveAdminUserId(organizationId)
            ?? throw new BusinessRuleException(ErrorCodes.TestToolsNoAdminUser,
                "Organizacija nema aktivnog korisnika u Admin grupi ovlasti (system_key 'admin') — seed nema u čije ime dodati podatke.");
        DemoSeedResultDto result = new DemoSeedResultDto { OrganizationId = organizationId, RunTag = NewTag() };
        // Postojeća Admin grupa se ne dira (ni članstvo): zaposlenik sa svim ovlastima dobiva novu grupu ovog prolaza.
        await Seed(organizationId, actorUserId, null, result.RunTag, result, SeedMode.ExistingFill);
        return result;
    }

    /// <summary>Puni organizaciju novim podacima u ime aktera; postojeće ne dira (nove poslovnice dobivaju svoje radno vrijeme i
    /// praznik, nova politika otkazivanja dodjeljuje se samo novoj poslovnici, nova grupa ovlasti samo novim korisnicima).</summary>
    private async Task Seed(Guid organizationId, Guid actorUserId, Guid? adminGrantGroupId, string tag, DemoSeedResultDto result, SeedMode mode)
    {
        using IDisposable clock = OrganizationClockContext.Use(organizationId);

        DemoSeedCountsDto counts = result.Counts;
        string Tagged(string name) => $"{name} #{tag}";
        bool withCatalog = mode != SeedMode.Basic;
        bool jumpClock = mode == SeedMode.FullDemo;

        OrganizationCalendar calendar = await _organizationCalendarService.GetCalendar(organizationId);
        DateOnly today = calendar.LocalDate(_timeProvider.GetUtcNow());
        DateOnly templateAnchor = today.AddDays(-120);

        // "Puni demo": skok se planira unaprijed — novi "danas" je četvrtak najmanje MinimumJumpDays dana kasnije; sve što mora
        // biti u tekućem tjednu nakon skoka (prošli dani s ishodom) zakazuje se sada, dok je to još budućnost.
        int jumpDays = jumpClock ? JumpDays(today) : 0;
        DateOnly finalToday = today.AddDays(jumpDays);
        DateOnly weekMonday = finalToday.AddDays(-(((int)finalToday.DayOfWeek + 6) % 7));

        // --- Poslovnice s radnim vremenom i jednim praznikom ---
        CompanyDto centar = await AddCompany(organizationId, actorUserId, Tagged("Studio Centar"), "Ilica 1, Zagreb", "#1E88E5", 0, templateAnchor, counts);
        CompanyDto jarun = await AddCompany(organizationId, actorUserId, Tagged("Studio Jarun"), "Jarunska 5, Zagreb", "#43A047", 1, templateAnchor, counts);
        List<Guid> bothCompanies = new() { centar.Id, jarun.Id };

        // Praznik izvan tjedana s rasporedom (iza sljedećeg tjedna), da ne preskače generirane termine.
        await _companyHolidayService.Create(organizationId, actorUserId, centar.Id, new CompanyHolidayCreateRequest
        {
            Date = finalToday.AddDays(30),
            Name = Tagged("Dan studija")
        });
        counts.CompanyHolidays++;

        // Mala dodatna poslovnica samo za plan članarine "Pop-up" (Puni demo): zatvara se prije skoka, pa obnova otvara stajanje.
        CompanyDto popup = jumpClock
            ? await AddCompany(organizationId, actorUserId, Tagged("Studio Pop-up"), "Savska 100, Zagreb", "#6D4C41", 2, templateAnchor, counts)
            : null;

        Catalog catalog = withCatalog ? await AddCatalog(organizationId, actorUserId, Tagged, today, centar, jarun, popup, counts) : null;

        // --- Zaposlenici s loginom: Admin grupa, Trener (own opseg), Recepcija (all opseg, bez K2), korisnik bez ovlasti ---
        EngagementTypeDto fullTime = await _engagementTypeService.Create(organizationId, actorUserId,
            new EngagementTypeCreateRequest { Name = Tagged("Puno radno vrijeme") });
        counts.EngagementTypes++;

        GrantGroupDto trainerGroup = await _grantGroupService.Create(organizationId, actorUserId,
            new GrantGroupCreateRequest { Name = Tagged("Treneri"), Grants = TrainerGrants.ToList() });
        counts.GrantGroups++;
        GrantGroupDto receptionGroup = await _grantGroupService.Create(organizationId, actorUserId, new GrantGroupCreateRequest
        {
            Name = Tagged("Recepcija bez K2 ovlasti"),
            Grants = Grants.Catalog.Select(g => g.Key).Where(k => !K2Grants.Contains(k)).ToList()
        });
        counts.GrantGroups++;
        if (adminGrantGroupId == null)
        {
            GrantGroupDto allGrantsGroup = await _grantGroupService.Create(organizationId, actorUserId, new GrantGroupCreateRequest
            {
                Name = Tagged("Sve ovlasti"),
                Grants = Grants.Catalog.Select(g => g.Key).ToList()
            });
            counts.GrantGroups++;
            adminGrantGroupId = allGrantsGroup.Id;
        }

        DateOnly employmentStart = today.AddYears(-1);
        List<Guid> NoServices() => new();

        Guid adminEmployee = await AddEmployee(organizationId, actorUserId, "Ana", "Administrator", tag, fullTime.Id, employmentStart,
            bothCompanies, centar.Id, catalog?.AllServiceIds ?? NoServices(), new List<Guid> { adminGrantGroupId.Value },
            "Admin grupa — svi grantovi", templateAnchor, result);
        Guid trainerEmployee = await AddEmployee(organizationId, actorUserId, "Marko", "Trener", tag, fullTime.Id, employmentStart,
            new List<Guid> { centar.Id }, centar.Id, catalog?.TrainerServiceIds ?? NoServices(), new List<Guid> { trainerGroup.Id },
            "Treneri — vlastiti termini (appointments.write.own), pregled klijenata", templateAnchor, result);
        Guid receptionEmployee = await AddEmployee(organizationId, actorUserId, "Iva", "Recepcija", tag, fullTime.Id, employmentStart,
            bothCompanies, jarun.Id, catalog?.ReceptionServiceIds ?? NoServices(), new List<Guid> { receptionGroup.Id },
            "Recepcija — all opseg, sve osim K2 grantova (korekcije, override radne snage, otpisi, roster i paketi unatrag)", templateAnchor, result);
        await AddEmployee(organizationId, actorUserId, "Petar", "Bez ovlasti", tag, fullTime.Id, employmentStart,
            new List<Guid> { centar.Id }, centar.Id, NoServices(), new List<Guid>(),
            "Bez ijedne grupe ovlasti — prijava radi, svaka zaštićena radnja vraća 403", templateAnchor, result);

        // --- Klijenti s GDPR suglasnošću (datum nikad u budućnosti) i rođendanima (dva u tjednu "danas") ---
        List<Guid> clients = new();
        for (int i = 0; i < ClientNames.Length; i++)
        {
            (string first, string last) = ClientNames[i];
            DateOnly dateOfBirth = i switch
            {
                0 => finalToday.AddYears(-30),            // rođendan "danas" (nakon eventualnog skoka sata)
                1 => finalToday.AddDays(3).AddYears(-27), // rođendan za tri dana
                _ => today.AddYears(-20 - i).AddDays(i * 11)
            };
            ClientDto client = await _clientService.Create(organizationId, actorUserId, new ClientCreateRequest
            {
                FirstName = first,
                LastName = $"{last} #{tag}",
                Email = $"{AsciiLower(first)}.{AsciiLower(last)}.{tag}@klijent.{EmailDomain}",
                Phone = $"+385 91 {100 + i:000} {1000 + i * 37:0000}",
                DateOfBirth = dateOfBirth,
                GdprConsentGiven = true,
                GdprConsentDate = today.AddDays(-i * 9),
                HomeCompanyId = i % 2 == 0 ? centar.Id : jarun.Id
            });
            clients.Add(client.Id);
            counts.Clients++;
        }

        if (catalog == null)
        {
            // "Osnova" završava ovdje: bez usluga, cjenika, paketa, članarina, grupa, termina i checkouta.
            result.LocalDate = today;
            return;
        }

        Staff staff = new(adminEmployee, trainerEmployee, receptionEmployee);
        await AddCommissionRules(organizationId, actorUserId, catalog, staff, counts);

        // --- Grupa s članovima (prvih 6 klijenata) ---
        GroupDto group = await _groupService.Create(organizationId, actorUserId, new GroupCreateRequest
        {
            Name = Tagged("Večernji grupni trening"),
            CompanyId = centar.Id,
            Note = "Demo grupa (seed).",
            Slots = new List<GroupSlotCreateRequest>
            {
                new() { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(18, 0) },
                new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(18, 0) },
                new() { DayOfWeek = DayOfWeek.Friday, StartTime = new TimeOnly(18, 0) }
            },
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new()
                {
                    ServiceId = catalog.GroupTraining.Id,
                    StartOffsetMinutes = 0,
                    RoomId = catalog.HallCentar.Id,
                    Capacity = 10,
                    EmployeeIds = new List<Guid> { trainerEmployee }
                }
            }
        });
        counts.Groups++;
        List<Guid> templateIds = group.SegmentTemplates.Select(t => t.Id).ToList();
        List<Guid> groupMembers = clients.Take(6).ToList();
        foreach (Guid clientId in groupMembers)
        {
            await _groupService.AddMember(organizationId, actorUserId, group.Id,
                new GroupMemberAddRequest { ClientId = clientId, SegmentTemplateIds = templateIds });
            counts.GroupMembers++;
        }

        SalesRun sales = new(organizationId, actorUserId, result, staff.Reception);
        if (jumpClock)
            await SeedFullDemoTimeline(sales, calendar, catalog, staff, centar, jarun, popup, group.Id, groupMembers, clients,
                today, jumpDays, weekMonday);
        else
            await SeedExistingOrganizationTimeline(sales, calendar, catalog, staff, centar, jarun, group.Id, clients, today);

        await Tally(sales, today.AddDays(-400), finalToday.AddDays(400), staff);
        result.ClockAdvancedDays = jumpDays;
        result.LocalDate = calendar.LocalDate(_timeProvider.GetUtcNow());
    }

    /// <summary>"Puni demo": faza 1 na početnom satu (prodaje, zakazano za tekući tjedan nakon skoka, zatvaranje pop-up
    /// poslovnice), skok sata kroz ITestToolsService.AdvanceClock (obnova dan po dan), faza 2 na novom "danas" (ishodi prošlih
    /// dana tekućeg tjedna, plaćanje zaostataka, budući termini).</summary>
    private async Task SeedFullDemoTimeline(
        SalesRun s, OrganizationCalendar calendar, Catalog catalog, Staff staff, CompanyDto centar, CompanyDto jarun, CompanyDto popup,
        Guid groupId, List<Guid> groupMembers, List<Guid> clients, DateOnly today, int jumpDays, DateOnly weekMonday)
    {
        DemoSeedCountsDto counts = s.Result.Counts;
        DateOnly finalToday = today.AddDays(jumpDays);
        DateTimeOffset At(DateOnly date, int hour) => calendar.ToInstant(date, TimeSpan.FromHours(hour));

        // ---------- Faza 1: početni sat ----------

        // Raspored tekućeg i sljedećeg tjedna nakon skoka (sada još budućnost): grupa pon–ned + 13 dana.
        List<(Guid AppointmentId, Guid SegmentId)> pastGroupOccurrences = new();
        await Optional(s.Result, "Generiranje termina grupe", async () =>
        {
            GenerateGroupAppointmentsResult generated = await _groupService.GenerateAppointments(s.OrganizationId, s.ActorUserId,
                new GenerateGroupAppointmentsRequest { GroupId = groupId, FromDate = weekMonday, ToDate = weekMonday.AddDays(13) });
            counts.GroupAppointments += generated.CreatedCount;
            pastGroupOccurrences.AddRange(generated.Created
                .Where(c => calendar.LocalDate(c.PlannedStart) < finalToday)
                .Select(c => (c.Id, c.Segments.Single().Id)));
        });

        // Individualni termini prošlih dana tekućeg tjedna (nakon skoka se odrađuju i naplaćuju kroz checkout) i dva bez ishoda.
        int nextClient = 6;
        Guid NextClient() => clients[nextClient++ % clients.Count];
        List<BookedSession> toComplete = new();
        for (DateOnly day = weekMonday; day < finalToday; day = day.AddDays(1))
        {
            toComplete.Add(await Book(s, centar.Id, catalog.Personal.Id, staff.Trainer, At(day, 9), NextClient()));
            toComplete.Add(await Book(s, jarun.Id, catalog.Physio.Id, staff.Reception, At(day, 12), NextClient()));
        }
        BookedSession unresolved1 = await Book(s, centar.Id, catalog.Consult.Id, staff.Admin, At(finalToday.AddDays(-1), 14), NextClient());
        BookedSession unresolved2 = await Book(s, jarun.Id, catalog.Massage.Id, staff.Reception, At(finalToday.AddDays(-2), 15), NextClient());
        counts.PastAppointments += new[] { unresolved1, unresolved2 }.Count(b => b != null);

        // Paketi prodani kroz checkout (provizija na prodaju recepciji) i sesije koje će ih trošiti (pon, uto, sri tekućeg tjedna).
        Guid? tenPack = await SellPackage(s, clients[TenPackClient], centar.Id, catalog.TenSessions.Id);
        Guid? threePack = await SellPackage(s, clients[ThreePackClient], centar.Id, catalog.ThreeSessions.Id);
        List<(BookedSession Session, Guid? ClientPackageId)> packageSessions = new();
        for (int i = 0; i < 3; i++)
        {
            DateOnly day = weekMonday.AddDays(i);
            packageSessions.Add((await Book(s, centar.Id, catalog.Personal.Id, staff.Trainer, At(day, 16), clients[TenPackClient]), tenPack));
            packageSessions.Add((await Book(s, centar.Id, catalog.Reformer.Id, staff.Admin, At(day, 17), clients[ThreePackClient]), threePack));
        }

        // Članarine "u prošlosti": aktivna (plaćena), s dugom (neplaćena), pauzirana (pauza počinje u tekućem tjednu nakon skoka),
        // otkazana (završava krajem prvog perioda) i pop-up (poslovnica se zatvara, pa obnova otvara stajanje).
        Guid? active = await SellMembership(s, clients[ActiveMemberClient], catalog.BasicPlan.Id, centar.Id, pay: true);
        await SellMembership(s, clients[AwaitingOrDebtMemberClient], catalog.PremiumPlan.Id, centar.Id, pay: false);
        Guid? paused = await SellMembership(s, clients[PausedMemberClient], catalog.FlexPlan.Id, centar.Id, pay: true);
        if (paused != null)
            await Optional(s.Result, "Pauza članstva", () => _clientMembershipService.Pause(s.OrganizationId, s.ActorUserId, paused.Value,
                new ClientMembershipPauseRequest { StartsOn = weekMonday, EndsOn = weekMonday.AddDays(20), Reason = "Putovanje (demo)." }));
        Guid? ended = await SellMembership(s, clients[EndedMemberClient], catalog.BasicPlan.Id, centar.Id, pay: true);
        if (ended != null)
            await Optional(s.Result, "Otkaz članstva", () => _clientMembershipService.RequestCancellation(s.OrganizationId, s.ActorUserId,
                ended.Value, new ClientMembershipCancelRequest { Reason = "Seli se (demo)." }));
        await SellMembership(s, clients[StandingStillMemberClient], catalog.PopupPlan.Id, popup.Id, pay: true);
        await Optional(s.Result, "Zatvaranje pop-up poslovnice", () => _companyService.SetActive(s.OrganizationId, s.ActorUserId, popup.Id, false));

        // ---------- Skok sata (isti mehanizam kao alat za pomak vremena: obnova za svaki preskočeni dan) ----------
        await _testToolsService.AdvanceClock(s.OrganizationId, new TestClockAdvanceRequest { Days = jumpDays }, platformAccountId: null);

        // ---------- Faza 2: novi "danas" ----------

        // Prošli individualni termini: odrađeni i naplaćeni kroz checkout (provizija za uslugu nastaje odrađivanjem).
        foreach (BookedSession session in toComplete.Where(x => x != null))
        {
            await Optional(s.Result, $"Odrađivanje termina {session.Start:yyyy-MM-dd HH:mm}", async () =>
            {
                await _bookingService.SetParticipationStatus(s.OrganizationId, s.ActorUserId, hasFullScope: true, session.ParticipationId,
                    new BookingSetStatusRequest { Status = BookingStatus.Completed });
                counts.PastAppointments++;
                await PayInCheckout(s, session.ClientId, session.CompanyId, async checkoutId =>
                    await _checkoutService.AddBookingItem(s.OrganizationId, s.ActorUserId, checkoutId,
                        new CheckoutAddBookingItemRequest { ParticipationId = session.ParticipationId }));
            });
        }

        // Sesije pokrivene paketom (odrađivanje s odabranim paketom troši jedinicu).
        foreach ((BookedSession session, Guid? clientPackageId) in packageSessions.Where(x => x.Session != null && x.ClientPackageId != null))
        {
            await Optional(s.Result, $"Odrađivanje paketne sesije {session.Start:yyyy-MM-dd HH:mm}", async () =>
            {
                await _bookingService.SetParticipationStatus(s.OrganizationId, s.ActorUserId, hasFullScope: true, session.ParticipationId,
                    new BookingSetStatusRequest { Status = BookingStatus.Completed, ClientPackageId = clientPackageId });
                counts.PastAppointments++;
            });
        }

        // Prisutnost na prošlim grupnim terminima tekućeg tjedna (zadnji član izostao), pa zatvaranje (provizija grupnog termina).
        foreach ((Guid appointmentId, Guid segmentId) in pastGroupOccurrences)
        {
            await Optional(s.Result, "Prisutnost na grupnom terminu", async () =>
            {
                for (int i = 0; i < groupMembers.Count; i++)
                {
                    bool attended = i < groupMembers.Count - 1;
                    await _groupAttendanceService.SetAttendance(s.OrganizationId, s.ActorUserId, hasFullScope: true, appointmentId,
                        new SetGroupAttendanceRequest
                        {
                            ClientId = groupMembers[i],
                            SegmentId = segmentId,
                            Attended = attended,
                            PaymentMethod = attended ? PaymentMethod.Cash : null
                        });
                    counts.GroupAttendances++;
                }
                await _appointmentService.Close(s.OrganizationId, s.ActorUserId, hasFullScope: true, appointmentId);
            });
        }

        // Aktivna i pauzirana članarina: klijent plaća zaduženja nastala obnovom tijekom skoka (stanje se vraća na "uredno").
        if (active != null)
            await PayOutstandingCharges(s, active.Value, clients[ActiveMemberClient], centar.Id);
        if (paused != null)
            await PayOutstandingCharges(s, paused.Value, clients[PausedMemberClient], centar.Id);

        // Budući individualni termini (sljedeća 4 dana).
        await FutureAppointments(s, calendar, catalog, staff, centar, jarun, finalToday, NextClient);
    }

    /// <summary>Dopuna postojeće organizacije: sat se NE pomiče (pomak u ne-demo organizaciji je trajan), pa samo stanja ostvariva
    /// danas; ostala stanja idu u Skipped s razlogom.</summary>
    private async Task SeedExistingOrganizationTimeline(
        SalesRun s, OrganizationCalendar calendar, Catalog catalog, Staff staff, CompanyDto centar, CompanyDto jarun, Guid groupId,
        List<Guid> clients, DateOnly today)
    {
        DemoSeedCountsDto counts = s.Result.Counts;
        DateTimeOffset At(DateOnly date, int hour) => calendar.ToInstant(date, TimeSpan.FromHours(hour));

        await Optional(s.Result, "Generiranje termina grupe", async () =>
        {
            GenerateGroupAppointmentsResult generated = await _groupService.GenerateAppointments(s.OrganizationId, s.ActorUserId,
                new GenerateGroupAppointmentsRequest { GroupId = groupId, FromDate = today.AddDays(1), ToDate = today.AddDays(14) });
            counts.GroupAppointments += generated.CreatedCount;
        });

        int nextClient = 6;
        Guid NextClient() => clients[nextClient++ % clients.Count];
        await FutureAppointments(s, calendar, catalog, staff, centar, jarun, today, NextClient);

        for (int day = 1; day <= 3; day++)
        {
            DateOnly date = today.AddDays(-day);
            await CompletedAppointment(s, centar.Id, catalog.Personal.Id, staff.Trainer, At(date, 9), NextClient());
            await CompletedAppointment(s, jarun.Id, catalog.Physio.Id, staff.Reception, At(date, 12), NextClient());
        }

        // Prošli termini bez ishoda (za označavanje dolaska / korekcije nakon zatvaranja dana).
        foreach ((Guid company, Guid service, Guid employee, DateTimeOffset start) in new[]
                 {
                     (centar.Id, catalog.Consult.Id, staff.Admin, At(today.AddDays(-1), 14)),
                     (jarun.Id, catalog.Massage.Id, staff.Reception, At(today.AddDays(-2), 15))
                 })
        {
            if (await Book(s, company, service, employee, start, NextClient()) != null)
                counts.PastAppointments++;
        }

        // Paketi kroz checkout; trošenje samo sesijama koje su danas već prošle (sat se ne pomiče, paket vrijedi od danas).
        Guid? tenPack = await SellPackage(s, clients[TenPackClient], centar.Id, catalog.TenSessions.Id);
        Guid? threePack = await SellPackage(s, clients[ThreePackClient], centar.Id, catalog.ThreeSessions.Id);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        int tenPackDone = 0, threePackDone = 0;
        for (int hour = 8; hour <= 11; hour++)
        {
            DateTimeOffset start = At(today, hour);
            if (tenPack != null && tenPackDone < 2 && start.AddMinutes(catalog.Personal.DefaultDurationMinutes) <= now
                && await CompletedPackageSession(s, centar.Id, catalog.Personal.Id, staff.Trainer, start, clients[TenPackClient], tenPack.Value))
                tenPackDone++;
            if (threePack != null && threePackDone < 3 && start.AddMinutes(catalog.Reformer.DefaultDurationMinutes) <= now
                && await CompletedPackageSession(s, centar.Id, catalog.Reformer.Id, staff.Admin, start, clients[ThreePackClient], threePack.Value))
                threePackDone++;
        }
        if (tenPackDone == 0)
            s.Result.Skipped.Add("Paket s potrošenim jedinicama: danas još nema prošlog termina unutar radnog vremena (sat se u postojećoj organizaciji ne pomiče).");
        if (threePackDone < 3)
            s.Result.Skipped.Add("Potrošen paket: danas nema dovoljno prošlih termina za sve jedinice (sat se u postojećoj organizaciji ne pomiče).");

        // Članarine ostvarive danas: aktivna i plaćena, neplaćena (čeka plaćanje, još nije dug), pauzirana od danas.
        await SellMembership(s, clients[ActiveMemberClient], catalog.BasicPlan.Id, centar.Id, pay: true);
        await SellMembership(s, clients[AwaitingOrDebtMemberClient], catalog.PremiumPlan.Id, centar.Id, pay: false);
        Guid? paused = await SellMembership(s, clients[PausedMemberClient], catalog.FlexPlan.Id, centar.Id, pay: true);
        if (paused != null)
            await Optional(s.Result, "Pauza članstva", () => _clientMembershipService.Pause(s.OrganizationId, s.ActorUserId, paused.Value,
                new ClientMembershipPauseRequest { StartsOn = today, EndsOn = today.AddDays(13), Reason = "Putovanje (demo)." }));

        s.Result.Skipped.Add("Članarina s dugom: dug nastaje tek istekom grace perioda nakon dospijeća — traži protok vremena (sat se u postojećoj organizaciji ne pomiče).");
        s.Result.Skipped.Add("Završeno članstvo: završava tek krajem perioda — traži protok vremena.");
        s.Result.Skipped.Add("Članstvo koje stoji (zatvorena poslovnica): stajanje otvara obnova na granici perioda — traži protok vremena, a zatvaranje postojeće poslovnice seed ne radi.");
    }

    private async Task FutureAppointments(
        SalesRun s, OrganizationCalendar calendar, Catalog catalog, Staff staff, CompanyDto centar, CompanyDto jarun, DateOnly fromToday,
        Func<Guid> nextClient)
    {
        for (int day = 1; day <= 4; day++)
        {
            DateOnly date = fromToday.AddDays(day);
            foreach ((Guid company, Guid service, Guid employee, int hour) in new[]
                     {
                         (centar.Id, catalog.Personal.Id, staff.Trainer, 9),
                         (centar.Id, catalog.Reformer.Id, staff.Admin, 10),
                         (jarun.Id, catalog.Massage.Id, staff.Reception, 11)
                     })
            {
                if (await Book(s, company, service, employee, calendar.ToInstant(date, TimeSpan.FromHours(hour)), nextClient()) != null)
                    s.Result.Counts.FutureAppointments++;
            }
        }
    }

    // ------------------------------------------------------------------ katalog ------------------------------------------------

    /// <summary>Katalog punog seeda (usluge, paketi, planovi) — ono što trebaju zaposlenici, raspored i prodaje.</summary>
    private sealed class Catalog
    {
        public RoomDto HallCentar { get; init; }
        public ServiceDto Personal { get; init; }
        public ServiceDto Reformer { get; init; }
        public ServiceDto Massage { get; init; }
        public ServiceDto Physio { get; init; }
        public ServiceDto Consult { get; init; }
        public ServiceDto GroupTraining { get; init; }
        public PackageDto TenSessions { get; init; }
        public PackageDto MonthlyPilates { get; init; }
        public PackageDto ThreeSessions { get; init; }
        public MembershipPlanDto BasicPlan { get; init; }
        public MembershipPlanDto PremiumPlan { get; init; }
        public MembershipPlanDto FlexPlan { get; init; }

        /// <summary>Samo "Puni demo" (opseg = pop-up poslovnica koja se zatvara).</summary>
        public MembershipPlanDto PopupPlan { get; init; }

        public List<Guid> AllServiceIds => new() { Personal.Id, Reformer.Id, Massage.Id, Physio.Id, Consult.Id, GroupTraining.Id };
        public List<Guid> TrainerServiceIds => new() { Personal.Id, Reformer.Id, GroupTraining.Id };
        public List<Guid> ReceptionServiceIds => new() { Massage.Id, Physio.Id, Consult.Id };
    }

    private sealed record Staff(Guid Admin, Guid Trainer, Guid Reception);

    private async Task<Catalog> AddCatalog(
        Guid organizationId, Guid actorUserId, Func<string, string> tagged, DateOnly today, CompanyDto centar, CompanyDto jarun,
        CompanyDto popup, DemoSeedCountsDto counts)
    {
        List<Guid> bothCompanies = new() { centar.Id, jarun.Id };

        // --- Prostorije i resursi ---
        RoomDto hallCentar = await AddRoom(organizationId, actorUserId, centar.Id, tagged("Dvorana A"), 15, counts);
        await AddRoom(organizationId, actorUserId, centar.Id, tagged("Sala B"), 6, counts);
        await AddRoom(organizationId, actorUserId, jarun.Id, tagged("Dvorana Jarun"), 12, counts);
        await AddRoom(organizationId, actorUserId, jarun.Id, tagged("Terapijska soba"), 2, counts);

        ResourceDto reformerCentar = await AddResource(organizationId, actorUserId, centar.Id, tagged("Reformer"), 4, counts);
        await AddResource(organizationId, actorUserId, centar.Id, tagged("Prostirka"), 20, counts);
        ResourceDto reformerJarun = await AddResource(organizationId, actorUserId, jarun.Id, tagged("Reformer"), 3, counts);
        await AddResource(organizationId, actorUserId, jarun.Id, tagged("Prostirka"), 15, counts);

        // --- Usluge (individualne i grupna), ponuđene u obje glavne poslovnice ---
        ServiceDto personal = await AddService(organizationId, actorUserId, tagged("Personalni trening"), ServiceExecutionMode.Individual, "#E53935", 60, 50m, bothCompanies, counts);
        ServiceDto reformer = await AddService(organizationId, actorUserId, tagged("Reformer pilates"), ServiceExecutionMode.Individual, "#8E24AA", 50, 40m, bothCompanies, counts);
        ServiceDto massage = await AddService(organizationId, actorUserId, tagged("Masaža"), ServiceExecutionMode.Individual, "#FB8C00", 45, 45m, bothCompanies, counts);
        ServiceDto physio = await AddService(organizationId, actorUserId, tagged("Fizioterapija"), ServiceExecutionMode.Individual, "#00897B", 30, 35m, bothCompanies, counts);
        ServiceDto consult = await AddService(organizationId, actorUserId, tagged("Konzultacije"), ServiceExecutionMode.Individual, "#546E7A", 30, 20m, bothCompanies, counts);
        ServiceDto groupTraining = await AddService(organizationId, actorUserId, tagged("Grupni trening"), ServiceExecutionMode.Group, "#FDD835", 60, 12m, bothCompanies, counts);

        await _serviceAvailabilityService.ReplaceDefaultResources(organizationId, actorUserId, reformer.Id, new ReplaceServiceDefaultResourcesRequest
        {
            Resources = new List<ServiceDefaultResourceRequest>
            {
                new() { ResourceId = reformerCentar.Id, QuantityRequired = 1 },
                new() { ResourceId = reformerJarun.Id, QuantityRequired = 1 }
            }
        });

        // --- Paketi ---
        PackageDto tenSessions = await _packageService.Create(organizationId, actorUserId, new PackageCreateRequest
        {
            Name = tagged("Paket 10 treninga"),
            Description = "10 ulazaka (personalni trening ili reformer), vrijedi 90 dana.",
            EntryMode = PackageEntryMode.SharedPool,
            TotalEntryCount = 10,
            ValidityType = PackageValidityType.DayCount,
            ValidityDays = 90,
            DefaultPrice = 450m,
            Services = new List<PackageServiceItemRequest> { new() { ServiceId = personal.Id }, new() { ServiceId = reformer.Id } }
        });
        counts.Packages++;
        PackageDto monthlyPilates = await _packageService.Create(organizationId, actorUserId, new PackageCreateRequest
        {
            Name = tagged("Mjesečni pilates"),
            Description = "8 reformer termina i neograničeno grupnih treninga, vrijedi 30 dana.",
            EntryMode = PackageEntryMode.PerService,
            ValidityType = PackageValidityType.DayCount,
            ValidityDays = 30,
            DefaultPrice = 280m,
            SortOrder = 1,
            Services = new List<PackageServiceItemRequest>
            {
                new() { ServiceId = reformer.Id, EntryCount = 8 },
                new() { ServiceId = groupTraining.Id, EntryCount = null }
            }
        });
        counts.Packages++;
        PackageDto threeSessions = await _packageService.Create(organizationId, actorUserId, new PackageCreateRequest
        {
            Name = tagged("Probni paket 3 treninga"),
            Description = "3 ulaska (personalni trening ili reformer), vrijedi 60 dana.",
            EntryMode = PackageEntryMode.SharedPool,
            TotalEntryCount = 3,
            ValidityType = PackageValidityType.DayCount,
            ValidityDays = 60,
            DefaultPrice = 120m,
            SortOrder = 2,
            Services = new List<PackageServiceItemRequest> { new() { ServiceId = personal.Id }, new() { ServiceId = reformer.Id } }
        });
        counts.Packages++;

        // --- Cjenik (cijene po poslovnici; zadane cijene su na uslugama i paketima) ---
        DateOnly validFrom = today.AddDays(-30);
        await AddPrice(organizationId, actorUserId, PricingSubjectType.Service, personal.Id, jarun.Id, 55m, validFrom, counts);
        await AddPrice(organizationId, actorUserId, PricingSubjectType.Service, massage.Id, jarun.Id, 50m, validFrom, counts);
        await AddPrice(organizationId, actorUserId, PricingSubjectType.Service, reformer.Id, centar.Id, 42m, validFrom, counts);
        await AddPrice(organizationId, actorUserId, PricingSubjectType.Package, tenSessions.Id, jarun.Id, 480m, validFrom, counts);

        // --- Planovi članarine ---
        MembershipPlanDto basicPlan = await _membershipPlanService.Create(organizationId, actorUserId, new MembershipPlanCreateRequest
        {
            Name = tagged("Članarina Basic"),
            Description = "Neograničeno grupnih treninga u svim poslovnicama.",
            Price = 49m,
            BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = groupTraining.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        counts.MembershipPlans++;
        MembershipPlanDto premiumPlan = await _membershipPlanService.Create(organizationId, actorUserId, new MembershipPlanCreateRequest
        {
            Name = tagged("Članarina Premium"),
            Description = "Grupni treninzi i reformer u poslovnici Centar, obnova prvog u mjesecu.",
            Price = 89m,
            StartFee = 20m,
            BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.CalendarMonth,
            CompanyScope = MembershipCompanyScope.SelectedCompanies,
            CompanyIds = new List<Guid> { centar.Id },
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = groupTraining.Id }, new() { ServiceId = reformer.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        counts.MembershipPlans++;
        MembershipPlanDto flexPlan = await _membershipPlanService.Create(organizationId, actorUserId, new MembershipPlanCreateRequest
        {
            Name = tagged("Članarina Flex"),
            Description = "Grupni treninzi u svim poslovnicama, pauza do 30 dana (produljuje period).",
            Price = 59m,
            BillingInterval = MembershipBillingInterval.Monthly,
            RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = groupTraining.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = true, MaxPauseDays = 30, MaxPausesPer12Months = 2, ExtendsPeriod = true }
        });
        counts.MembershipPlans++;
        MembershipPlanDto popupPlan = null;
        if (popup != null)
        {
            popupPlan = await _membershipPlanService.Create(organizationId, actorUserId, new MembershipPlanCreateRequest
            {
                Name = tagged("Članarina Pop-up"),
                Description = "Grupni treninzi samo u pop-up poslovnici (koja je zatvorena — članstvo stoji).",
                Price = 29m,
                BillingInterval = MembershipBillingInterval.Monthly,
                RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
                CompanyScope = MembershipCompanyScope.SelectedCompanies,
                CompanyIds = new List<Guid> { popup.Id },
                Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = groupTraining.Id } },
                Pause = new MembershipPauseRulesDto { Allowed = false }
            });
            counts.MembershipPlans++;
        }

        // --- Politika otkazivanja s naknadom (nova, dodijeljena samo novoj poslovnici) i razlozi otkazivanja ---
        CancellationPolicyDto strictPolicy = await _cancellationPolicyService.Create(organizationId, actorUserId, new CancellationPolicyCreateRequest
        {
            Name = tagged("Otkazivanje 24h s naknadom"),
            CancellationWindowMinutes = 1440,
            LateCancellation = new CancellationPolicyEventRuleDto { FeeType = CancellationFeeType.Fixed, FeeValue = 15m, PackageAction = CancellationPackageAction.None },
            NoShow = new CancellationPolicyEventRuleDto { FeeType = CancellationFeeType.Fixed, FeeValue = 25m, PackageAction = CancellationPackageAction.ConsumeUnit }
        });
        counts.CancellationPolicies++;
        await _cancellationPolicyService.Assign(organizationId, actorUserId, new CancellationPolicyAssignmentRequest
        {
            CompanyId = centar.Id,
            CancellationPolicyId = strictPolicy.Id
        });

        await AddReason(organizationId, actorUserId, tagged("Bolest"), 0, client: true, business: false, noShow: false, counts);
        await AddReason(organizationId, actorUserId, tagged("Privatne obveze"), 1, client: true, business: false, noShow: false, counts);
        await AddReason(organizationId, actorUserId, tagged("Trener spriječen"), 2, client: false, business: true, noShow: false, counts);
        await AddReason(organizationId, actorUserId, tagged("Nije se javio"), 3, client: false, business: false, noShow: true, counts);

        return new Catalog
        {
            HallCentar = hallCentar,
            Personal = personal,
            Reformer = reformer,
            Massage = massage,
            Physio = physio,
            Consult = consult,
            GroupTraining = groupTraining,
            TenSessions = tenSessions,
            MonthlyPilates = monthlyPilates,
            ThreeSessions = threeSessions,
            BasicPlan = basicPlan,
            PremiumPlan = premiumPlan,
            FlexPlan = flexPlan,
            PopupPlan = popupPlan
        };
    }

    /// <summary>Pravila provizija (ADR-0010 + ADR-0030): opće pravilo svakog zaposlenika za individualne usluge, pravilo usluge s
    /// prednošću (trener, personalni), grupni trening (trener) i prodaja paketa i članarina (recepcija). Bez datuma važenja.</summary>
    private async Task AddCommissionRules(Guid organizationId, Guid actorUserId, Catalog catalog, Staff staff, DemoSeedCountsDto counts)
    {
        async Task Rule(CommissionRuleCreateRequest request)
        {
            await _commissionRuleService.Create(organizationId, actorUserId, request);
            counts.CommissionRules++;
        }

        CommissionRuleCreateRequest General(Guid employeeId, decimal percent) => new()
        {
            EmployeeId = employeeId,
            SubjectType = CommissionSubjectType.AllServices,
            Tiers = new List<CommissionRuleTierDto> { new() { FromRevenue = 0m, CalculationType = CommissionCalculationType.Percentage, Value = percent } }
        };

        await Rule(General(staff.Admin, 10m));
        await Rule(General(staff.Trainer, 15m));
        await Rule(General(staff.Reception, 10m));
        await Rule(new CommissionRuleCreateRequest
        {
            EmployeeId = staff.Trainer, SubjectType = CommissionSubjectType.Service, ServiceId = catalog.Personal.Id,
            CalculationType = CommissionCalculationType.Fixed, Value = 10m
        });
        await Rule(new CommissionRuleCreateRequest
        {
            EmployeeId = staff.Trainer, SubjectType = CommissionSubjectType.Service, ServiceId = catalog.GroupTraining.Id,
            CalculationType = CommissionCalculationType.Fixed, Value = 15m
        });
        foreach (PackageDto package in new[] { catalog.TenSessions, catalog.MonthlyPilates, catalog.ThreeSessions })
            await Rule(new CommissionRuleCreateRequest
            {
                EmployeeId = staff.Reception, SubjectType = CommissionSubjectType.Package, PackageId = package.Id,
                CalculationType = CommissionCalculationType.Percentage, Value = 10m
            });
        foreach (MembershipPlanDto plan in new[] { catalog.BasicPlan, catalog.PremiumPlan, catalog.FlexPlan, catalog.PopupPlan }.Where(p => p != null))
            await Rule(new CommissionRuleCreateRequest
            {
                EmployeeId = staff.Reception, SubjectType = CommissionSubjectType.MembershipPlan, MembershipPlanId = plan.Id,
                CalculationType = CommissionCalculationType.Fixed, Value = 10m
            });
    }

    // ------------------------------------------------------------------ prodaje i naplata --------------------------------------

    /// <summary>Kontekst prodaja jednog prolaza: akter, rezultat, prodavač (korisnik provizije na prodaju), prodana članstva i paketi.</summary>
    private sealed class SalesRun
    {
        public SalesRun(Guid organizationId, Guid actorUserId, DemoSeedResultDto result, Guid seller)
        {
            OrganizationId = organizationId;
            ActorUserId = actorUserId;
            Result = result;
            Seller = seller;
        }

        public Guid OrganizationId { get; }
        public Guid ActorUserId { get; }
        public DemoSeedResultDto Result { get; }
        public Guid Seller { get; }
        public List<Guid> Memberships { get; } = new();
        public List<(Guid ClientId, Guid ClientPackageId)> ClientPackages { get; } = new();
    }

    private sealed record BookedSession(Guid AppointmentId, Guid ParticipationId, Guid ClientId, Guid CompanyId, DateTimeOffset Start);

    /// <summary>Prodaja paketa kroz checkout (stavka paketa, prodavač = recepcija, plaćanje gotovinom, zatvaranje izdaje paket).</summary>
    private async Task<Guid?> SellPackage(SalesRun s, Guid clientId, Guid companyId, Guid packageId)
    {
        Guid? clientPackageId = null;
        await Optional(s.Result, "Prodaja paketa", async () =>
        {
            CheckoutDto completed = await PayInCheckout(s, clientId, companyId, async checkoutId =>
            {
                CheckoutDto withItem = await _checkoutService.AddPackageItem(s.OrganizationId, s.ActorUserId, checkoutId,
                    new CheckoutAddPackageItemRequest { PackageId = packageId });
                CheckoutItemDto item = withItem.Items.Single(i => i.PackageId == packageId);
                await _checkoutService.SetItemSaleCommissionEmployee(s.OrganizationId, s.ActorUserId, checkoutId, item.Id,
                    new SaleCommissionEmployeeRequest { EmployeeId = s.Seller });
            });
            clientPackageId = completed.Items.Single(i => i.PackageId == packageId).ClientPackageId;
            if (clientPackageId != null)
            {
                s.ClientPackages.Add((clientId, clientPackageId.Value));
                s.Result.Counts.ClientPackagesSold++;
            }
        });
        return clientPackageId;
    }

    /// <summary>Prodaja članarine (prodavač = recepcija); uz plaćanje sva otvorena zaduženja (prvi period, početna naknada) idu
    /// kroz checkout — tada nastaje i provizija na prvu prodaju.</summary>
    private async Task<Guid?> SellMembership(SalesRun s, Guid clientId, Guid planId, Guid soldCompanyId, bool pay)
    {
        Guid? membershipId = null;
        await Optional(s.Result, "Prodaja članarine", async () =>
        {
            ClientMembershipDto sold = await _clientMembershipService.Sell(s.OrganizationId, s.ActorUserId, clientId, new ClientMembershipSellRequest
            {
                MembershipPlanId = planId,
                SoldCompanyId = soldCompanyId,
                ProposedSaleCommissionEmployeeId = s.Seller
            });
            membershipId = sold.Id;
            s.Memberships.Add(sold.Id);
            s.Result.Counts.MembershipsSold++;
        });
        if (pay && membershipId != null)
            await PayOutstandingCharges(s, membershipId.Value, clientId, soldCompanyId);
        return membershipId;
    }

    /// <summary>Plaćanje svih otvorenih zaduženja članstva u jednom checkoutu.</summary>
    private Task PayOutstandingCharges(SalesRun s, Guid membershipId, Guid clientId, Guid companyId) =>
        Optional(s.Result, "Plaćanje zaduženja članarine", async () =>
        {
            List<MembershipChargeDto> open = (await _clientMembershipService.GetCharges(s.OrganizationId, membershipId))
                .Where(c => c.OutstandingAmount > 0 && !c.InOpenCheckout)
                .OrderBy(c => c.DueOn)
                .ToList();
            if (open.Count == 0)
                return;
            await PayInCheckout(s, clientId, companyId, async checkoutId =>
            {
                foreach (MembershipChargeDto charge in open)
                    await _checkoutService.AddMembershipChargeItem(s.OrganizationId, s.ActorUserId, checkoutId,
                        new CheckoutAddMembershipChargeItemRequest { MembershipChargeId = charge.Id });
            });
        });

    /// <summary>Checkout: otvori, dodaj stavke, plati cijeli preostali iznos gotovinom, zatvori.</summary>
    private async Task<CheckoutDto> PayInCheckout(SalesRun s, Guid clientId, Guid companyId, Func<Guid, Task> addItems)
    {
        CheckoutDto checkout = await _checkoutService.Create(s.OrganizationId, s.ActorUserId,
            new CheckoutCreateRequest { ClientId = clientId, CompanyId = companyId });
        await addItems(checkout.Id);
        CheckoutDto current = await _checkoutService.GetById(s.OrganizationId, checkout.Id);
        if (current.Totals.OutstandingAmount > 0)
            await _checkoutService.RecordPayment(s.OrganizationId, s.ActorUserId, checkout.Id,
                new CheckoutPaymentCreateRequest { Amount = current.Totals.OutstandingAmount, Method = PaymentMethod.Cash, Note = "Demo naplata (seed)." });
        CheckoutDto completed = await _checkoutService.Complete(s.OrganizationId, s.ActorUserId, checkout.Id);
        s.Result.Counts.CheckoutsCompleted++;
        return completed;
    }

    /// <summary>Na kraju: stanja prodanih članstava (čitaju se iz servisa), potrošene jedinice paketa i provizije seedanih
    /// zaposlenika.</summary>
    private async Task Tally(SalesRun s, DateOnly from, DateOnly to, Staff staff)
    {
        DemoSeedCountsDto counts = s.Result.Counts;
        foreach (Guid membershipId in s.Memberships)
        {
            ClientMembershipDto m = await _clientMembershipService.GetById(s.OrganizationId, membershipId);
            switch (m.State)
            {
                case MembershipState.Ended:
                    counts.MembershipsEnded++;
                    break;
                case MembershipState.StandingStill:
                    counts.MembershipsStandingStill++;
                    break;
                case MembershipState.Paused:
                    counts.MembershipsPaused++;
                    break;
                case MembershipState.Active or MembershipState.Scheduled when m.Standing == MembershipStanding.Delinquent:
                    counts.MembershipsInDebt++;
                    break;
                case MembershipState.Active or MembershipState.Scheduled when m.OutstandingAmount > 0:
                    counts.MembershipsAwaitingPayment++;
                    break;
                case MembershipState.Active or MembershipState.Scheduled:
                    counts.MembershipsActivePaid++;
                    break;
            }
        }

        foreach ((Guid clientId, Guid clientPackageId) in s.ClientPackages)
        {
            ClientPackageDto package = await _clientPackageService.GetById(s.OrganizationId, clientId, clientPackageId);
            counts.PackageUnitsConsumed += (package.TotalEntryCount ?? 0) - (package.RemainingSharedEntries ?? 0);
        }

        foreach (Guid employeeId in new[] { staff.Admin, staff.Trainer, staff.Reception })
        {
            PagedResult<CommissionEntryDto> entries = await _commissionService.GetEntries(s.OrganizationId,
                new CommissionEntryQuery { EmployeeId = employeeId, From = from, To = to, Page = 1, PageSize = 1 });
            counts.CommissionEntries += entries.TotalCount;
        }
    }

    // ------------------------------------------------------------------ termini ------------------------------------------------

    /// <summary>Zakazani individualni termin s jednim klijentom (null kad ga pravilo odbije — razlog u Skipped).</summary>
    private async Task<BookedSession> Book(SalesRun s, Guid companyId, Guid serviceId, Guid employeeId, DateTimeOffset start, Guid clientId)
    {
        BookedSession booked = null;
        await Optional(s.Result, $"Termin {start:yyyy-MM-dd HH:mm}", async () =>
        {
            AppointmentDto appointment = await _appointmentService.Create(s.OrganizationId, s.ActorUserId, hasFullScope: true, new AppointmentCreateRequest
            {
                CompanyId = companyId,
                Note = "Demo termin (seed).",
                Segments = new List<AppointmentSegmentCreateRequest>
                {
                    new()
                    {
                        ServiceId = serviceId,
                        PlannedStart = start,
                        EmployeeIds = new List<Guid> { employeeId },
                        Participants = new List<AppointmentParticipantCreateRequest> { new() { ClientId = clientId } }
                    }
                }
            });
            Guid participationId = appointment.Bookings.Single().Participations.Single().Id;
            booked = new BookedSession(appointment.Id, participationId, clientId, companyId, start);
        });
        return booked;
    }

    /// <summary>Prošli odrađeni termin kroz "upiši odrađeno" (K1-1: prošlost dopuštena, validira se kao budućnost), plaćen gotovinom.</summary>
    private Task CompletedAppointment(SalesRun s, Guid companyId, Guid serviceId, Guid employeeId, DateTimeOffset start, Guid clientId) =>
        Optional(s.Result, $"Odrađeni termin {start:yyyy-MM-dd HH:mm}", async () =>
        {
            await _appointmentService.CompleteNow(s.OrganizationId, s.ActorUserId, hasFullScope: true, new AppointmentCompleteNowRequest
            {
                CompanyId = companyId,
                Note = "Demo odrađeni termin (seed).",
                Segment = new AppointmentSegmentDefinitionRequest
                {
                    ServiceId = serviceId,
                    PlannedStart = start,
                    EmployeeIds = new List<Guid> { employeeId }
                },
                Clients = new List<AppointmentCompletedClientRequest>
                {
                    new() { ClientId = clientId, PaymentMethod = PaymentMethod.Cash, IsPaid = true }
                }
            });
            s.Result.Counts.PastAppointments++;
        });

    /// <summary>Današnja već prošla sesija "upiši odrađeno" pokrivena paketom (troši jedinicu).</summary>
    private async Task<bool> CompletedPackageSession(
        SalesRun s, Guid companyId, Guid serviceId, Guid employeeId, DateTimeOffset start, Guid clientId, Guid clientPackageId)
    {
        bool done = false;
        await Optional(s.Result, $"Paketna sesija {start:yyyy-MM-dd HH:mm}", async () =>
        {
            await _appointmentService.CompleteNow(s.OrganizationId, s.ActorUserId, hasFullScope: true, new AppointmentCompleteNowRequest
            {
                CompanyId = companyId,
                Note = "Demo sesija iz paketa (seed).",
                Segment = new AppointmentSegmentDefinitionRequest
                {
                    ServiceId = serviceId,
                    PlannedStart = start,
                    EmployeeIds = new List<Guid> { employeeId }
                },
                Clients = new List<AppointmentCompletedClientRequest> { new() { ClientId = clientId, ClientPackageId = clientPackageId } }
            });
            s.Result.Counts.PastAppointments++;
            done = true;
        });
        return done;
    }

    // ------------------------------------------------------------------ osnovni gradivni blokovi ------------------------------

    private async Task<CompanyDto> AddCompany(
        Guid organizationId, Guid actorUserId, string name, string address, string color, int sortOrder, DateOnly templateAnchor,
        DemoSeedCountsDto counts)
    {
        CompanyDto company = await _companyService.Create(organizationId, actorUserId, new CompanyCreateRequest
        {
            Name = name,
            Address = address,
            Phone = "+385 1 555 0100",
            ColorHex = color,
            Country = "HR",
            SortOrder = sortOrder
        });
        counts.Companies++;
        await _workingHoursTemplateService.UpsertForCompany(organizationId, actorUserId, company.Id,
            WeeklyHours(templateAnchor, new TimeOnly(7, 0), new TimeOnly(21, 0)));
        return company;
    }

    private async Task<RoomDto> AddRoom(Guid organizationId, Guid actorUserId, Guid companyId, string name, int capacity, DemoSeedCountsDto counts)
    {
        RoomDto room = await _roomService.Create(organizationId, actorUserId,
            new RoomCreateRequest { CompanyId = companyId, Name = name, Capacity = capacity });
        counts.Rooms++;
        return room;
    }

    private async Task<ResourceDto> AddResource(Guid organizationId, Guid actorUserId, Guid companyId, string name, int capacity, DemoSeedCountsDto counts)
    {
        ResourceDto resource = await _resourceService.Create(organizationId, actorUserId,
            new ResourceCreateRequest { CompanyId = companyId, Name = name, Capacity = capacity });
        counts.Resources++;
        return resource;
    }

    private async Task<ServiceDto> AddService(
        Guid organizationId, Guid actorUserId, string name, ServiceExecutionMode mode, string color, int duration, decimal price,
        List<Guid> companyIds, DemoSeedCountsDto counts)
    {
        ServiceDto service = await _serviceCatalogService.Create(organizationId, actorUserId, new ServiceCreateRequest
        {
            Name = name,
            ExecutionMode = mode,
            ColorHex = color,
            DefaultDurationMinutes = duration,
            DefaultPrice = price,
            SortOrder = counts.Services
        });
        counts.Services++;
        await _serviceAvailabilityService.ReplaceAssignedCompanies(organizationId, actorUserId, service.Id,
            new ReplaceServiceCompaniesRequest { CompanyIds = companyIds });
        return service;
    }

    private async Task AddPrice(
        Guid organizationId, Guid actorUserId, PricingSubjectType subjectType, Guid subjectId, Guid companyId, decimal price,
        DateOnly validFrom, DemoSeedCountsDto counts)
    {
        await _pricingService.Create(organizationId, actorUserId, new PriceListItemCreateRequest
        {
            SubjectType = subjectType,
            ServiceId = subjectType == PricingSubjectType.Service ? subjectId : null,
            PackageId = subjectType == PricingSubjectType.Package ? subjectId : null,
            CompanyId = companyId,
            Price = price,
            ValidFrom = validFrom
        });
        counts.PriceListItems++;
    }

    private async Task AddReason(
        Guid organizationId, Guid actorUserId, string name, int sortOrder, bool client, bool business, bool noShow, DemoSeedCountsDto counts)
    {
        await _cancellationReasonService.Create(organizationId, actorUserId, new CancellationReasonUpsertRequest
        {
            Name = name,
            SortOrder = sortOrder,
            AppliesToClientCancellation = client,
            AppliesToBusinessCancellation = business,
            AppliesToNoShow = noShow
        });
        counts.CancellationReasons++;
    }

    private async Task<Guid> AddEmployee(
        Guid organizationId, Guid actorUserId, string firstName, string lastName, string tag, Guid engagementTypeId,
        DateOnly employmentStart, List<Guid> companyIds, Guid primaryCompanyId, List<Guid> serviceIds, List<Guid> grantGroupIds,
        string description, DateOnly templateAnchor, DemoSeedResultDto result)
    {
        string password = NewPassword();
        EmployeeWithLoginCreateResponse created = await _employeeService.CreateWithLogin(organizationId, actorUserId, new EmployeeWithLoginCreateRequest
        {
            FirstName = firstName,
            LastName = $"{lastName} #{tag}",
            Email = $"{AsciiLower(firstName)}.{AsciiLower(lastName)}.{tag}@{EmailDomain}",
            EmploymentStartDate = employmentStart,
            EngagementTypeId = engagementTypeId,
            CompanyIds = companyIds,
            PrimaryCompanyId = primaryCompanyId,
            ServiceIds = serviceIds,
            Password = password,
            GrantGroupIds = grantGroupIds
        });
        result.Counts.Employees++;
        result.Users.Add(new DemoSeedUserDto { UserId = created.UserId, Email = created.Email, Password = password, Description = description });

        await _workingHoursTemplateService.UpsertForEmployee(organizationId, actorUserId, created.EmployeeId,
            WeeklyHours(templateAnchor, new TimeOnly(8, 0), new TimeOnly(20, 0)));
        return created.EmployeeId;
    }

    /// <summary>Opcionalan dio seeda: poslovno odbijanje (npr. sudar u rasporedu postojeće organizacije) se ne propagira, nego se
    /// navodi u <see cref="DemoSeedResultDto.Skipped"/>.</summary>
    private static async Task Optional(DemoSeedResultDto result, string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is BusinessRuleException or ValidationAppException or ForbiddenAppException or NotFoundAppException)
        {
            result.Skipped.Add($"{what}: {ex.Message}");
        }
    }

    /// <summary>Dužina skoka "Punog demoa": najmanje MinimumJumpDays, do prvog četvrtka (tekući tjedan ima prošle dane).</summary>
    private static int JumpDays(DateOnly today)
    {
        int days = MinimumJumpDays;
        while (today.AddDays(days).DayOfWeek != JumpTargetDay)
            days++;
        return days;
    }

    private static WorkingHoursTemplateUpsertRequest WeeklyHours(DateOnly anchor, TimeOnly start, TimeOnly end) => new()
    {
        CycleType = WorkingHoursCycleType.Weekly,
        AnchorDate = anchor,
        Intervals = Enum.GetValues<DayOfWeek>()
            .Select(day => new WorkingHoursIntervalRequest { CycleWeekIndex = 0, DayOfWeek = day, StartTime = start, EndTime = end })
            .ToList()
    };

    private static string NewTag() => RandomNumberGenerator.GetHexString(6, lowercase: true);

    private static string NewPassword() => "Demo-" + RandomNumberGenerator.GetString(PasswordAlphabet, 12);

    /// <summary>Ime za email: mala slova bez dijakritika.</summary>
    private static string AsciiLower(string value) => new string(value.ToLowerInvariant()
        .Replace('č', 'c').Replace('ć', 'c').Replace('š', 's').Replace('ž', 'z').Replace('đ', 'd')
        .Where(char.IsAsciiLetterOrDigit).ToArray());
}
