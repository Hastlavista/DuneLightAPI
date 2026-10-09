#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Checkouts;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Utils;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Notifications;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// One isolated tenant per test (its own Organization, so parallel test classes never see each other's rows — same
/// isolation pattern as the existing DB-backed tests), seeded with the minimum a real Appointment flow needs:
/// Company + working hours, Service (30 min, price 50) available at the Company, Employee (working hours, assigned to
/// the Company and the Service), one Client and an admin actor User.
///
/// The world seeds REFERENCE data straight into the database (that is setup, not behaviour under test) and drives all
/// BEHAVIOUR under test through the real public services (<see cref="Appointments"/>, <see cref="Bookings"/>, ...).
/// State is verified by reading the persisted rows back through a fresh DbContext (see the Load*/Query helpers), not
/// from returned DTOs alone.
///
/// Time: every calendar value is a fixed constant. <see cref="FutureDay"/> (2031) is far enough ahead that it is
/// always "in the future" relative to the real clock and never inside any cancellation cutoff; <see cref="PastDay"/>
/// (2020) is always in the past. Both are UTC — the production overlap/working-hours code works on the offset the
/// caller supplies, so UTC keeps the arithmetic unambiguous. The two tests that need "now-relative" times (late
/// cancellation, guest attendance after start) say so explicitly.
/// </summary>
public sealed class SchedulingWorld : IAsyncDisposable
{
    public static readonly DateTimeOffset FutureDay = new(2031, 3, 3, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset PastDay = new(2020, 3, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Hours covered by the seeded working-hours templates (every day of the week).</summary>
    public static readonly TimeSpan WorkStart = TimeSpan.FromHours(8);
    public static readonly TimeSpan WorkEnd = TimeSpan.FromHours(20);

    public const decimal DefaultServicePrice = 50m;
    public const int DefaultServiceDuration = 30;

    private static int _sequence;

    private readonly IServiceScope _scope;

    public Guid OrganizationId { get; }
    public Guid ActorUserId { get; private set; }
    public Company Company { get; private set; }
    public ServiceEntity Service { get; private set; }
    public Employee Employee { get; private set; }
    public Client Client { get; private set; }

    private readonly List<Guid> _employeeIds = new();

    private SchedulingWorld(string testName)
    {
        OrganizationId = Guid.NewGuid();
        TestName = testName;
        _scope = SchedulingTestHost.CreateScope();
    }

    public string TestName { get; }

    #region Public services under test (real implementations)

    public IAppointmentService Appointments => _scope.ServiceProvider.GetRequiredService<IAppointmentService>();
    public IBookingService Bookings => _scope.ServiceProvider.GetRequiredService<IBookingService>();
    public IGroupService Groups => _scope.ServiceProvider.GetRequiredService<IGroupService>();
    public IGroupAttendanceService GroupAttendance => _scope.ServiceProvider.GetRequiredService<IGroupAttendanceService>();
    public IWaitlistService Waitlist => _scope.ServiceProvider.GetRequiredService<IWaitlistService>();
    public ICheckoutService Checkouts => _scope.ServiceProvider.GetRequiredService<ICheckoutService>();
    public IClientPackageService ClientPackages => _scope.ServiceProvider.GetRequiredService<IClientPackageService>();
    public T Resolve<T>() => _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>T1-7: poslovni sat OVE organizacije postavljen na <paramref name="instant"/> (+1 ms po čitanju, vidi TestClock) dok se
    /// rezultat ne odbaci — isti mehanizam kao pomak sata u Managementu (pomak po organizaciji + kontekst sata).</summary>
    public IDisposable ClockAt(DateTimeOffset instant)
    {
        IClockOffsetStore offsets = Resolve<IClockOffsetStore>();
        TimeSpan previous = offsets.GetOffset(OrganizationId);
        offsets.Set(OrganizationId, instant - TestClock.UtcNow);
        IDisposable context = OrganizationClockContext.Use(OrganizationId);
        return new ClockRestore(() =>
        {
            context.Dispose();
            offsets.Set(OrganizationId, previous);
        });
    }

    private sealed class ClockRestore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    #endregion

    #region Creation / cleanup

    /// <summary>The Organization's business timezone defaults to UTC: every calendar constant in this suite
    /// (<see cref="FutureDay"/>, <see cref="Future"/>, working hours 08-20) is a UTC wall-clock value, so the characterization
    /// tests describe scheduling in a UTC business calendar. Timezone-specific tests pass an IANA id explicitly.</summary>
    public static async Task<SchedulingWorld> Create(string testName, string timeZone = "UTC")
    {
        SchedulingWorld world = new(testName);
        try
        {
            await world.SeedBaseline(timeZone);
        }
        catch
        {
            // A half-seeded tenant must not leak into the shared database.
            await world.DisposeAsync();
            throw;
        }

        return world;
    }

    private async Task SeedBaseline(string timeZone)
    {
        await using DatabaseContext db = NewDb();

        db.Organizations.Add(new Organization
        {
            Id = OrganizationId,
            Name = $"SchedTest-{TestName}",
            Slug = $"sched-test-{OrganizationId:N}",
            TimeZone = timeZone,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();

        db.EngagementTypes.Add(new EngagementType
        {
            Id = EngagementTypeId,
            OrganizationId = OrganizationId,
            Name = "Full time",
            IsActive = true,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();

        ActorUserId = await AddUser(db);
        // K2 (ADR-0032): rad izvan radnog vremena je zaseban grant (ne više "full scope"). Akter suite modelira osoblje s punim
        // ovlastima nad rasporedom (pozivi s hasFullScope: true), pa ga ima od početka; testovi odbijanja koriste AddMemberUser.
        await GrantUser(ActorUserId, Core.Shared.Grants.AppointmentsAvailabilityOverride);
        await SeedDefaultCancellationPolicy(db);

        Company = await AddCompany("Main company");
        Service = await AddService(DefaultServiceDuration, DefaultServicePrice, name: "Main service");
        Employee = await AddEmployee(name: "Main employee");
        Client = await AddClient("Main", "Client");
    }

    private Guid EngagementTypeId { get; } = Guid.NewGuid();

    public DatabaseContext NewDb() => DatabaseContext.GenerateContext(SchedulingTestHost.ConnectionString);

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await DeleteOrganization(OrganizationId);
    }

    /// <summary>Removes every row of one organization (e.g. a world's, or one created through registration).</summary>
    public static async Task DeleteOrganization(Guid org)
    {
        // Direct SQL keyed on the organization. session_replication_role = replica suspends FK triggers for the
        // transaction so deletion order does not matter (the connection user is the database owner, as in every other
        // DB-backed test here). Child tables that carry no organization_id are removed through their parent first.
        await using DatabaseContext db = DatabaseContext.GenerateContext(SchedulingTestHost.ConnectionString);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");

        string[] childDeletes =
        {
            "DELETE FROM dunelight.appointment_audit_log WHERE appointment_id IN (SELECT id FROM dunelight.appointments WHERE organization_id = {0})",
            "DELETE FROM dunelight.appointment_segment_employees WHERE appointment_segment_id IN (SELECT id FROM dunelight.appointment_segments WHERE organization_id = {0})",
            "DELETE FROM dunelight.appointment_segment_resources WHERE appointment_segment_id IN (SELECT id FROM dunelight.appointment_segments WHERE organization_id = {0})",
            "DELETE FROM dunelight.checkout_audit_log WHERE checkout_id IN (SELECT id FROM dunelight.checkouts WHERE organization_id = {0})",
            "DELETE FROM dunelight.payment_allocations WHERE payment_id IN (SELECT id FROM dunelight.payments WHERE organization_id = {0})",
            "DELETE FROM dunelight.client_package_service_entries WHERE client_package_id IN (SELECT id FROM dunelight.client_packages WHERE organization_id = {0})",
            "DELETE FROM dunelight.package_services WHERE package_id IN (SELECT id FROM dunelight.packages WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_audit_log WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_member_segment_templates WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.user_grant_groups WHERE grant_group_id IN (SELECT id FROM dunelight.grant_groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.grant_group_grants WHERE grant_group_id IN (SELECT id FROM dunelight.grant_groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_members WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_segment_template_resources WHERE group_segment_template_id IN (SELECT t.id FROM dunelight.group_segment_templates t JOIN dunelight.groups g ON g.id = t.group_id WHERE g.organization_id = {0})",
            "DELETE FROM dunelight.group_segment_template_employees WHERE group_segment_template_id IN (SELECT t.id FROM dunelight.group_segment_templates t JOIN dunelight.groups g ON g.id = t.group_id WHERE g.organization_id = {0})",
            "DELETE FROM dunelight.group_segment_templates WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_slots WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_companies WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_services WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_audit_log WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.roster_audit_log WHERE roster_entry_id IN (SELECT id FROM dunelight.roster_entries WHERE organization_id = {0})",
            "DELETE FROM dunelight.service_companies WHERE service_id IN (SELECT id FROM dunelight.services WHERE organization_id = {0})",
            "DELETE FROM dunelight.service_default_resources WHERE service_id IN (SELECT id FROM dunelight.services WHERE organization_id = {0})",
            "DELETE FROM dunelight.working_hours_intervals WHERE working_hours_template_id IN (SELECT id FROM dunelight.working_hours_templates WHERE organization_id = {0})",
            "DELETE FROM dunelight.price_list_item_history WHERE price_list_item_id IN (SELECT id FROM dunelight.price_list_items WHERE organization_id = {0})",
            "DELETE FROM dunelight.client_tag_assignments WHERE client_id IN (SELECT id FROM dunelight.clients WHERE organization_id = {0})"
        };

        foreach (string sql in childDeletes)
            await db.Database.ExecuteSqlRawAsync(sql, org);

        // Every remaining table with an organization_id column (discovered from the catalog, so a table added later
        // is cleaned too), organizations last.
        List<string> orgTables = await db.Database
            .SqlQueryRaw<string>(
                @"SELECT table_name AS ""Value"" FROM information_schema.columns
                  WHERE table_schema = 'dunelight' AND column_name = 'organization_id' AND table_name <> 'organizations'")
            .ToListAsync();

        // The table names come from information_schema (never from input); only the organization id is a parameter.
#pragma warning disable EF1002
        foreach (string table in orgTables)
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM dunelight.\"{table}\" WHERE organization_id = {{0}}", org);
#pragma warning restore EF1002

        await db.Database.ExecuteSqlRawAsync("DELETE FROM dunelight.organizations WHERE id = {0}", org);
        await tx.CommitAsync();
    }

    #endregion

    #region Time helpers

    /// <summary>T1-7: kalendarski dan (DateOnly) zidnog datuma vrijednosti — za DateOnly ugovore (cjenik, generiranje grupa, ...).</summary>
    public static DateOnly Day(DateTimeOffset value) => DateOnly.FromDateTime(value.Date);

    public static DateTimeOffset Future(int hour, int minute = 0) => FutureDay.AddHours(hour).AddMinutes(minute);
    public static DateTimeOffset Past(int hour, int minute = 0) => PastDay.AddHours(hour).AddMinutes(minute);

    #endregion

    #region Reference-data seeding

    private async Task<Guid> AddUser(DatabaseContext db)
    {
        int n = Interlocked.Increment(ref _sequence);
        Guid id = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = id,
            OrganizationId = OrganizationId,
            Email = $"user{n}-{id:N}@sched.test",
            PasswordHash = "not-a-real-hash",
            ApiKey = $"sched-test-key-{id:N}",
            IsActive = true,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<Company> AddCompany(string name, bool isActive = true, bool withWorkingHours = true)
    {
        await using DatabaseContext db = NewDb();
        Company company = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Name = $"{name}-{Guid.NewGuid():N}",
            Country = "HR",
            IsActive = isActive,
            CreatedAt = TestClock.UtcNow
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        if (withWorkingHours)
            await AddWorkingHours(db, employeeId: null, companyId: company.Id);

        return company;
    }

    public async Task<ServiceEntity> AddService(
        int durationMinutes, decimal price, bool isActive = true, bool availableAtCompany = true, Guid? companyId = null,
        string name = "Service", ServiceExecutionMode mode = ServiceExecutionMode.Individual)
    {
        await using DatabaseContext db = NewDb();
        ServiceEntity service = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Name = $"{name}-{Guid.NewGuid():N}",
            ExecutionMode = mode,
            DefaultDurationMinutes = durationMinutes,
            DefaultPrice = price,
            IsActive = isActive,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        };
        db.Services.Add(service);
        await db.SaveChangesAsync();

        if (availableAtCompany)
        {
            db.ServiceCompanies.Add(new ServiceCompany
            {
                Id = Guid.NewGuid(),
                ServiceId = service.Id.Value,
                CompanyId = companyId ?? Company.Id.Value
            });
            await db.SaveChangesAsync();
        }

        return service;
    }

    /// <summary>Employee that, by default, is active, has working hours, is assigned to <see cref="Company"/> and to
    /// <see cref="Service"/> — i.e. eligible for the default appointment. Each eligibility link can be switched off.</summary>
    public async Task<Employee> AddEmployee(
        string name = "Employee", bool isActive = true, bool assignedToCompany = true, bool assignedToService = true,
        bool withWorkingHours = true, Guid? companyId = null, Guid? serviceId = null)
    {
        await using DatabaseContext db = NewDb();
        Guid userId = await AddUser(db);
        Guid employeeId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = employeeId,
            OrganizationId = OrganizationId,
            FirstName = name,
            LastName = "Tester",
            EmploymentStartDate = new DateOnly(2019, 1, 1),
            EngagementTypeId = EngagementTypeId,
            IsActive = isActive,
            UserId = userId,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();

        if (assignedToCompany)
            db.EmployeeCompanies.Add(new EmployeeCompany
            {
                Id = Guid.NewGuid(), EmployeeId = employeeId, CompanyId = companyId ?? Company.Id.Value, IsPrimary = true
            });

        if (assignedToService)
            db.EmployeeServiceAssignments.Add(new EmployeeServiceAssignment
            {
                Id = Guid.NewGuid(), EmployeeId = employeeId, ServiceId = serviceId ?? Service.Id.Value
            });

        await db.SaveChangesAsync();

        if (withWorkingHours)
            await AddWorkingHours(db, employeeId, companyId: null);

        _employeeIds.Add(employeeId);
        return await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
    }

    /// <summary>M1G: an employee NOT allowed to perform the world's service — restricted to another service (an employee with
    /// no service assignments may perform every service).</summary>
    public async Task<Employee> AddEmployeeRestrictedToAnotherService(string name = "Unauthorized")
    {
        ServiceEntity other = await AddService(30, 10m, name: "Other service");
        return await AddEmployee(name, serviceId: other.Id);
    }

    public async Task MakeServiceAvailableAt(ServiceEntity service, Company company)
    {
        await using DatabaseContext db = NewDb();
        db.ServiceCompanies.Add(new ServiceCompany { Id = Guid.NewGuid(), ServiceId = service.Id.Value, CompanyId = company.Id.Value });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A MANUAL (POS) payment: Checkout → Booking item → Payment, through the real ICheckoutService. This is the path that
    /// creates <c>Payment.IsCheckInGenerated = false</c>, as opposed to the payments created during check-in/completion.
    /// The checkout is left Open unless <paramref name="complete"/> is set (only possible when fully paid).
    /// </summary>
    public async Task<Guid> PayBookingViaCheckout(Guid bookingId, Client client, decimal amount, PaymentMethod method = PaymentMethod.Cash, bool complete = false)
    {
        Core.DTOs.Checkouts.CheckoutDto checkout = await Checkouts.Create(
            OrganizationId, ActorUserId, new Core.DTOs.Checkouts.CheckoutCreateRequest { ClientId = client.Id.Value, CompanyId = Company.Id.Value });
        await Checkouts.AddBookingItem(OrganizationId, ActorUserId, checkout.Id, new Core.DTOs.Checkouts.CheckoutAddBookingItemRequest { ParticipationId = await SingleParticipationOfBooking(bookingId) });
        await Checkouts.RecordPayment(OrganizationId, ActorUserId, checkout.Id,
            new Core.DTOs.Checkouts.CheckoutPaymentCreateRequest { Amount = amount, Method = method });
        if (complete)
            await Checkouts.Complete(OrganizationId, ActorUserId, checkout.Id);
        return checkout.Id;
    }

    /// <summary>The ONLY participation of a booking (one-segment fixtures).</summary>
    public async Task<Guid> SingleParticipationOfBooking(Guid bookingId)
    {
        await using DatabaseContext db = NewDb();
        return Assert.Single(await db.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.BookingId == bookingId).Select(p => p.Id).ToListAsync()).Value;
    }

    /// <summary>Employee → Service capability (an Employee can be authorized for several Services).</summary>
    public async Task AssignEmployeeToService(Employee employee, ServiceEntity service)
    {
        await using DatabaseContext db = NewDb();
        db.EmployeeServiceAssignments.Add(new EmployeeServiceAssignment
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id.Value, ServiceId = service.Id.Value
        });
        await db.SaveChangesAsync();
    }

    public async Task AssignEmployeeToCompany(Employee employee, Company company)
    {
        await using DatabaseContext db = NewDb();
        db.EmployeeCompanies.Add(new EmployeeCompany
        {
            Id = Guid.NewGuid(), EmployeeId = employee.Id.Value, CompanyId = company.Id.Value, IsPrimary = false
        });
        await db.SaveChangesAsync();
    }

    private async Task AddWorkingHours(DatabaseContext db, Guid? employeeId, Guid? companyId, TimeSpan? start = null, TimeSpan? end = null)
    {
        Guid templateId = Guid.NewGuid();
        WorkingHoursTemplate template = new()
        {
            Id = templateId,
            OrganizationId = OrganizationId,
            EmployeeId = employeeId,
            CompanyId = companyId,
            CycleType = WorkingHoursCycleType.Weekly,
            AnchorDate = new DateOnly(2020, 1, 6),
            CreatedAt = TestClock.UtcNow
        };
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            template.Intervals.Add(new WorkingHoursInterval
            {
                Id = Guid.NewGuid(),
                WorkingHoursTemplateId = templateId,
                CycleWeekIndex = 0,
                DayOfWeek = day,
                StartTime = TimeOnly.FromTimeSpan(start ?? WorkStart),
                EndTime = TimeOnly.FromTimeSpan(end ?? WorkEnd)
            });

        db.WorkingHoursTemplates.Add(template);
        await db.SaveChangesAsync();
    }

    /// <summary>Replaces an owner's working hours with a single daily window (every weekday), e.g. to make a slot fall outside them.</summary>
    public async Task SetWorkingHours(Employee employee, TimeSpan start, TimeSpan end)
    {
        await using DatabaseContext db = NewDb();
        await ReplaceTemplate(db, employee.Id, null, start, end);
    }

    public async Task SetCompanyWorkingHours(Company company, TimeSpan start, TimeSpan end)
    {
        await using DatabaseContext db = NewDb();
        await ReplaceTemplate(db, null, company.Id, start, end);
    }

    private async Task ReplaceTemplate(DatabaseContext db, Guid? employeeId, Guid? companyId, TimeSpan start, TimeSpan end)
    {
        List<WorkingHoursTemplate> old = await db.WorkingHoursTemplates
            .Where(t => t.OrganizationId == OrganizationId && t.EmployeeId == employeeId && t.CompanyId == companyId)
            .Include(t => t.Intervals)
            .ToListAsync();
        db.WorkingHoursIntervals.RemoveRange(old.SelectMany(t => t.Intervals));
        db.WorkingHoursTemplates.RemoveRange(old);
        await db.SaveChangesAsync();
        await AddWorkingHours(db, employeeId, companyId, start, end);
    }

    public async Task<Client> AddClient(string firstName = "Client", string lastName = "Tester", bool isActive = true, bool isAnonymized = false)
    {
        await using DatabaseContext db = NewDb();
        Client client = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            MemberNumber = Interlocked.Increment(ref _sequence) + (int)(Environment.TickCount64 % 100000) * 1000,
            FirstName = firstName,
            LastName = lastName,
            IsActive = isActive,
            IsAnonymized = isAnonymized,
            GdprConsentGiven = true,
            CreatedAt = TestClock.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    /// <summary>M1D: the default capacity 2 holds exactly one individual appointment (1 employee + 1 client) — an overlapping
    /// second one in the same room exceeds it.</summary>
    public async Task<Room> AddRoom(Company company = null, bool isActive = true, int capacity = 2)
    {
        await using DatabaseContext db = NewDb();
        Room room = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            CompanyId = (company ?? Company).Id.Value,
            Name = $"Room-{Guid.NewGuid():N}",
            Capacity = capacity,
            IsActive = isActive,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    /// <summary>M1D: a company-scoped physical resource (catalog row) with the given capacity.</summary>
    public async Task<Resource> AddResource(Company company = null, int capacity = 3, bool isActive = true, string name = null)
    {
        await using DatabaseContext db = NewDb();
        Resource resource = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            CompanyId = (company ?? Company).Id.Value,
            Name = name ?? $"Resource-{Guid.NewGuid():N}",
            Capacity = capacity,
            IsActive = isActive,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return resource;
    }

    /// <summary>Price-list row (Service, optionally company-specific) effective from <paramref name="validFrom"/>.</summary>
    public async Task AddPriceListItem(
        ServiceEntity service, decimal price, DateOnly validFrom, Guid? companyId = null, DateOnly? validTo = null, Guid? employeeId = null)
    {
        await using DatabaseContext db = NewDb();
        db.PriceListItems.Add(new PriceListItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            ServiceId = service.Id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            Price = price,
            ValidFrom = validFrom,
            ValidTo = validTo,
            IsActive = true,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Changes the live Service (name/duration/price) — used to prove which values are snapshotted.</summary>
    public async Task UpdateService(ServiceEntity service, int? durationMinutes = null, decimal? defaultPrice = null, string name = null)
    {
        await using DatabaseContext db = NewDb();
        ServiceEntity tracked = await db.Services.SingleAsync(s => s.Id == service.Id);
        if (durationMinutes.HasValue) tracked.DefaultDurationMinutes = durationMinutes.Value;
        if (defaultPrice.HasValue) tracked.DefaultPrice = defaultPrice.Value;
        if (name != null) tracked.Name = name;
        await db.SaveChangesAsync();
    }

    public async Task SetCompanyActive(Company company, bool isActive)
    {
        await using DatabaseContext db = NewDb();
        Company tracked = await db.Companies.SingleAsync(c => c.Id == company.Id);
        tracked.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task SetServiceActive(ServiceEntity service, bool isActive)
    {
        await using DatabaseContext db = NewDb();
        ServiceEntity tracked = await db.Services.SingleAsync(s => s.Id == service.Id);
        tracked.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public async Task SetClientActive(Client client, bool isActive)
    {
        await using DatabaseContext db = NewDb();
        Client tracked = await db.Clients.SingleAsync(c => c.Id == client.Id);
        tracked.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    /// <summary>P1: a whole-appointment cancellation is always Business and needs a reason.</summary>
    public static AppointmentCancelRequest BusinessCancel(string reason = "test cancellation") =>
        new() { CancellationInitiator = CancellationInitiator.Business, CancellationReason = reason };

    /// <summary>P1: a Booking-wide / participation cancellation requested by the client (policy evaluated).</summary>
    public static BookingCancelRequest ClientCancel(string reason = null) =>
        new() { CancellationInitiator = CancellationInitiator.Client, CancellationReason = reason };

    /// <summary>P1: a Booking-wide / participation cancellation by the studio (appointments.write.all + reason, no policy).</summary>
    public static BookingCancelRequest BusinessBookingCancel(string reason = "test cancellation") =>
        new() { CancellationInitiator = CancellationInitiator.Business, CancellationReason = reason };

    /// <summary>P1: the organization's cancellation window lives on its DEFAULT policy — publishes a new neutral version
    /// (no fee, no package penalty) of the default policy with the given window.</summary>
    public Task SetCancellationWindowMinutes(int minutes) => PublishDefaultPolicyVersion(minutes);

    /// <summary>P1: publishes a new (immutable) version of the organization's default cancellation policy, exactly as
    /// ICancellationPolicyService.PublishVersion would (reference data, so seeded directly).</summary>
    public async Task PublishDefaultPolicyVersion(
        int windowMinutes,
        CancellationFeeType lateFeeType = CancellationFeeType.None, decimal? lateFeeValue = null,
        CancellationPackageAction latePackageAction = CancellationPackageAction.None,
        CancellationFeeType noShowFeeType = CancellationFeeType.None, decimal? noShowFeeValue = null,
        CancellationPackageAction noShowPackageAction = CancellationPackageAction.None,
        CancellationMembershipAction? lateMembershipAction = null, CancellationMembershipAction? noShowMembershipAction = null)
    {
        await PublishPolicyVersion(DefaultPolicyId, windowMinutes, lateFeeType, lateFeeValue, latePackageAction,
            noShowFeeType, noShowFeeValue, noShowPackageAction, lateMembershipAction, noShowMembershipAction);
    }

    public async Task PublishPolicyVersion(
        Guid policyId, int windowMinutes,
        CancellationFeeType lateFeeType = CancellationFeeType.None, decimal? lateFeeValue = null,
        CancellationPackageAction latePackageAction = CancellationPackageAction.None,
        CancellationFeeType noShowFeeType = CancellationFeeType.None, decimal? noShowFeeValue = null,
        CancellationPackageAction noShowPackageAction = CancellationPackageAction.None,
        CancellationMembershipAction? lateMembershipAction = null, CancellationMembershipAction? noShowMembershipAction = null)
    {
        await using DatabaseContext db = NewDb();
        int next = (await db.CancellationPolicyVersions.Where(v => v.CancellationPolicyId == policyId).MaxAsync(v => (int?)v.Version) ?? 0) + 1;
        db.CancellationPolicyVersions.Add(new CancellationPolicyVersion
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, CancellationPolicyId = policyId, Version = next,
            CancellationWindowMinutes = windowMinutes,
            LateCancellationFeeType = lateFeeType, LateCancellationFeeValue = lateFeeValue, LateCancellationPackageAction = latePackageAction,
            LateCancellationMembershipAction = CancellationPolicyRules.MembershipActionFor(lateMembershipAction, lateFeeType, lateFeeValue),
            NoShowFeeType = noShowFeeType, NoShowFeeValue = noShowFeeValue, NoShowPackageAction = noShowPackageAction,
            NoShowMembershipAction = CancellationPolicyRules.MembershipActionFor(noShowMembershipAction, noShowFeeType, noShowFeeValue),
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>P1: the organization default (neutral: 1440 min, None/None) that registration creates — seeded the same way.</summary>
    public Guid DefaultPolicyId { get; private set; }

    private async Task SeedDefaultCancellationPolicy(DatabaseContext db)
    {
        DefaultPolicyId = Guid.NewGuid();
        db.CancellationPolicies.Add(new CancellationPolicy
        {
            Id = DefaultPolicyId, OrganizationId = OrganizationId, Name = "Zadana politika", IsActive = true,
            IsOrganizationDefault = true, CreatedAt = TestClock.UtcNow
        });
        db.CancellationPolicyVersions.Add(new CancellationPolicyVersion
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, CancellationPolicyId = DefaultPolicyId, Version = 1,
            CancellationWindowMinutes = 1440,
            LateCancellationFeeType = CancellationFeeType.None, LateCancellationPackageAction = CancellationPackageAction.None,
            NoShowFeeType = CancellationFeeType.None, NoShowPackageAction = CancellationPackageAction.None,
            // P2 (pregled 2D #8): politika bez naknade ne kažnjava ni članove.
            LateCancellationMembershipAction = CancellationMembershipAction.ReturnCreditChargeFee,
            NoShowMembershipAction = CancellationMembershipAction.ReturnCreditChargeFee,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<RosterType> AddRosterType(string name, bool isAbsence)
    {
        await using DatabaseContext db = NewDb();
        RosterType type = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Name = $"{name}-{Guid.NewGuid():N}",
            CountsAsWork = !isAbsence,
            IsAbsence = isAbsence,
            RequiresTime = !isAbsence,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        };
        db.RosterTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    /// <summary>Whole-day absence (e.g. sick leave) for <paramref name="employee"/> on the single day <paramref name="day"/>
    /// (DateFrom = DateTo). See <see cref="AddOpenEndedAbsence"/> for the DateTo = null shape.</summary>
    public Task AddAbsence(Employee employee, DateTimeOffset day) => AddAbsenceRange(employee, day, day);

    /// <summary>Absence with no end date: production code treats <c>DateTo == null</c> as "absent from DateFrom onwards".</summary>
    public Task AddOpenEndedAbsence(Employee employee, DateTimeOffset from) => AddAbsenceRange(employee, from, null);

    public async Task AddAbsenceRange(Employee employee, DateTimeOffset from, DateTimeOffset? to)
    {
        RosterType type = await AddRosterType("Absence", isAbsence: true);
        await using DatabaseContext db = NewDb();
        db.RosterEntries.Add(new RosterEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            EmployeeId = employee.Id.Value,
            RosterTypeId = type.Id.Value,
            DateFrom = DateOnly.FromDateTime(from.Date),
            DateTo = to.HasValue ? DateOnly.FromDateTime(to.Value.Date) : null,
            IsOverride = false,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task AddScheduleBreak(Employee employee, DateTimeOffset startsAt, int durationMinutes)
    {
        await using DatabaseContext db = NewDb();
        db.ScheduleBreaks.Add(new ScheduleBreak
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            EmployeeId = employee.Id.Value,
            CompanyId = Company.Id.Value,
            StartsAt = startsAt,
            DurationMinutes = durationMinutes,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task AddCompanyHoliday(Company company, DateTimeOffset day)
    {
        await using DatabaseContext db = NewDb();
        db.CompanyHolidays.Add(new CompanyHoliday
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            CompanyId = company.Id.Value,
            Date = DateOnly.FromDateTime(day.Date),
            Name = "Holiday",
            IsAutoGenerated = false,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task AddCommissionRule(Employee employee, ServiceEntity service, CommissionCalculationType type, decimal value)
    {
        await using DatabaseContext db = NewDb();
        db.CommissionRules.Add(new CommissionRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            EmployeeId = employee.Id.Value,
            SubjectType = CommissionSubjectType.Service,
            ServiceId = service.Id,
            CalculationType = type,
            Value = value,
            IsActive = true,
            CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task DeactivateCommissionRules()
    {
        await using DatabaseContext db = NewDb();
        foreach (CommissionRule rule in await db.CommissionRules.Where(r => r.OrganizationId == OrganizationId).ToListAsync())
            rule.IsActive = false;
        await db.SaveChangesAsync();
    }

    /// <summary>Client package covering <paramref name="service"/>. <paramref name="entries"/> = remaining entries
    /// (null = unlimited). SharedPool keeps one pooled counter; PerService keeps the counter on the service entry.</summary>
    /// <summary>D3B3A.1: convenience for existing tests — the expiry's calendar date in its own offset becomes ValidUntilDate.</summary>
    public Task<ClientPackage> AddClientPackage(
        Client client, ServiceEntity service, int? entries, DateTimeOffset expiry,
        PackageEntryMode mode = PackageEntryMode.PerService, ClientPackageStatus status = ClientPackageStatus.Active) =>
        AddClientPackage(client, service, entries, DateOnly.FromDateTime(expiry.DateTime), mode, status);

    public async Task<ClientPackage> AddClientPackage(
        Client client, ServiceEntity service, int? entries, DateOnly validUntil,
        PackageEntryMode mode = PackageEntryMode.PerService, ClientPackageStatus status = ClientPackageStatus.Active)
    {
        await using DatabaseContext db = NewDb();

        Package package = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            Name = $"Package-{Guid.NewGuid():N}",
            EntryMode = mode,
            TotalEntryCount = mode == PackageEntryMode.SharedPool ? entries : null,
            ValidityType = PackageValidityType.FixedDate,
            ValidityFixedDate = validUntil,
            DefaultPrice = 100m,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = TestClock.UtcNow
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        Guid clientPackageId = Guid.NewGuid();
        ClientPackage clientPackage = new()
        {
            Id = clientPackageId,
            OrganizationId = OrganizationId,
            ClientId = client.Id.Value,
            PackageId = package.Id.Value,
            // CHANGED in T1 (T1-9): paket pokriva tek od PurchaseDate — fiksni dan kupnje je prije svih dana suite (PastDay 2020-03-02),
            // pa testovi i dalje opisuju pokriće unutar valjanosti (prije: 2025-01-01, kad donja granica nije postojala).
            PurchaseDate = new DateOnly(2020, 1, 1),
            PaidPrice = 100m,
            EntryMode = mode,
            TotalEntryCount = mode == PackageEntryMode.SharedPool ? entries : null,
            RemainingSharedEntries = mode == PackageEntryMode.SharedPool ? entries : null,
            ValidityType = PackageValidityType.FixedDate,
            ValidUntilDate = validUntil,
            Status = status,
            CreatedAt = TestClock.UtcNow
        };
        clientPackage.ServiceEntries.Add(new ClientPackageServiceEntry
        {
            Id = Guid.NewGuid(),
            ClientPackageId = clientPackageId,
            ServiceId = service.Id.Value,
            TotalEntries = mode == PackageEntryMode.PerService ? entries : null,
            RemainingEntries = mode == PackageEntryMode.PerService ? entries : null
        });
        db.ClientPackages.Add(clientPackage);
        await db.SaveChangesAsync();
        return clientPackage;
    }

    #endregion

    #region Request builders

    /// <summary>M1H: test-only one-segment spec (<see cref="TestAppointmentSpec"/>) — produces the target create request.</summary>
    public TestAppointmentSpec CreateRequest(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, ServiceEntity service = null,
        Company company = null, Room room = null, decimal? amount = null, bool overrideAvailability = false, string note = null,
        params Client[] extraClients)
    {
        List<Guid> clientIds = new() { (client ?? Client).Id.Value };
        clientIds.AddRange(extraClients.Select(c => c.Id.Value));

        return new TestAppointmentSpec
        {
            StartsAt = startsAt,
            ServiceId = (service ?? Service).Id.Value,
            EmployeeId = (employee ?? Employee).Id.Value,
            CompanyId = (company ?? Company).Id.Value,
            RoomId = room?.Id,
            ClientIds = clientIds,
            Amount = amount,
            OverrideAvailability = overrideAvailability,
            Note = note
        };
    }

    public Task<AppointmentDto> CreateAppointment(TestAppointmentSpec request, bool hasFullScope = true) =>
        Appointments.Create(OrganizationId, ActorUserId, hasFullScope, request.ToTarget());

    /// <summary>Convenience: individual appointment for the default Service/Employee/Company through the real Create flow.</summary>
    public Task<AppointmentDto> CreateAppointment(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, Room room = null, params Client[] extraClients) =>
        CreateAppointment(CreateRequest(startsAt, client, employee, room: room, extraClients: extraClients));

    /// <summary>"Complete now" spec (POST /complete): the one-segment spec plus one settlement per client.</summary>
    public TestCompletionSpec CompleteRequest(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, ServiceEntity service = null,
        Company company = null, Room room = null, decimal? amount = null, bool overrideAvailability = false,
        PaymentMethod? paymentMethod = null, bool isPaid = true, Guid? clientPackageId = null, decimal? settlementAmount = null,
        params AppointmentCompletedClientRequest[] settlements)
    {
        TestAppointmentSpec create = CreateRequest(startsAt, client, employee, service, company, room, amount, overrideAvailability);
        return new TestCompletionSpec
        {
            StartsAt = create.StartsAt,
            ServiceId = create.ServiceId,
            EmployeeId = create.EmployeeId,
            CompanyId = create.CompanyId,
            RoomId = create.RoomId,
            ClientIds = settlements is { Length: > 0 } ? settlements.Select(x => x.ClientId).ToList() : create.ClientIds,
            Amount = create.Amount,
            OverrideAvailability = overrideAvailability,
            Settlements = settlements is { Length: > 0 }
                ? settlements.ToList()
                : new List<AppointmentCompletedClientRequest>
                {
                    new()
                    {
                        ClientId = create.ClientIds[0],
                        PaymentMethod = paymentMethod,
                        IsPaid = isPaid,
                        ClientPackageId = clientPackageId,
                        Amount = settlementAmount ?? amount
                    }
                }
        };
    }

    public Task<AppointmentDto> CompleteNew(TestCompletionSpec request, bool hasFullScope = true) =>
        Appointments.CompleteNow(OrganizationId, ActorUserId, hasFullScope, request.ToCompleteNow());

    /// <summary>
    /// M1H: completes an EXISTING one-segment individual appointment through the target participation API — for every
    /// settlement, the client's participation on the only segment is (optionally re-priced via
    /// <c>PATCH /participations/{id}/price</c> and then) moved to Completed with that settlement
    /// (<c>PATCH /participations/{id}/status</c>). Returns the refreshed appointment.
    /// </summary>
    public async Task<AppointmentDto> CompleteParticipations(Guid appointmentId, TestCompletionSpec request, bool hasFullScope = true)
    {
        foreach (AppointmentCompletedClientRequest settlement in request.Settlements)
        {
            Guid participationId = await ParticipationIdOnOnlySegment(appointmentId, settlement.ClientId);
            if (settlement.Amount.HasValue)
                await Bookings.SetParticipationPrice(OrganizationId, ActorUserId, hasFullScope, participationId,
                    new ParticipationPriceChangeRequest { Amount = settlement.Amount });
            await Bookings.SetParticipationStatus(OrganizationId, ActorUserId, hasFullScope, participationId, new BookingSetStatusRequest
            {
                Status = BookingStatus.Completed,
                ClientPackageId = settlement.ClientPackageId,
                PaymentMethod = settlement.ClientPackageId.HasValue ? null : settlement.PaymentMethod,
                IsPaid = settlement.IsPaid
            });
        }
        return await Appointments.GetById(OrganizationId, appointmentId);
    }

    /// <summary>M1H: moves the ONLY segment of an appointment to a new start, keeping its duration (target
    /// <c>PATCH /segments/{id}/time</c>).</summary>
    public async Task<AppointmentDto> MoveOnlySegment(Guid appointmentId, DateTimeOffset plannedStart, bool hasFullScope = true, Guid? userId = null)
    {
        Appointment appointment = await LoadAppointment(appointmentId);
        return await Appointments.ChangeSegmentTime(OrganizationId, userId ?? ActorUserId, hasFullScope,
            Assert.Single(appointment.Segments).Id.Value, new AppointmentSegmentTimeChangeRequest { PlannedStart = plannedStart });
    }

    /// <summary>M1H: adds a client to the ONLY segment of an individual appointment (target <c>POST /{id}/clients</c>).</summary>
    public async Task<AppointmentDto> AddClientToOnlySegment(Guid appointmentId, Client client, decimal? amount = null, bool hasFullScope = true)
    {
        Appointment appointment = await LoadAppointment(appointmentId);
        return await Appointments.AddClient(OrganizationId, ActorUserId, hasFullScope, appointmentId, new AppointmentClientAddRequest
        {
            ClientId = client.Id.Value,
            Participations = new List<AppointmentClientParticipationRequest>
            {
                new() { SegmentId = Assert.Single(appointment.Segments).Id.Value, Amount = amount }
            }
        });
    }

    /// <summary>M1H: manual final price of the client's participation on the only segment (target
    /// <c>PATCH /participations/{id}/price</c>; null = back to the suggested price).</summary>
    public async Task<BookingDto> SetParticipationPrice(Guid appointmentId, Client client, decimal? amount, bool hasFullScope = true, Guid? userId = null) =>
        await Bookings.SetParticipationPrice(OrganizationId, userId ?? ActorUserId, hasFullScope,
            await ParticipationIdOnOnlySegment(appointmentId, client.Id.Value), new ParticipationPriceChangeRequest { Amount = amount });

    /// <summary>M1H: removes the client's participation on the only segment (target <c>DELETE /participations/{id}</c>).</summary>
    public async Task<AppointmentDto> RemoveClientFromOnlySegment(Guid appointmentId, Client client, bool hasFullScope = true) =>
        await Appointments.RemoveParticipation(OrganizationId, ActorUserId, hasFullScope, await ParticipationIdOnOnlySegment(appointmentId, client.Id.Value));

    /// <summary>The client's participation on the appointment's ONLY segment (asserts one segment).</summary>
    public async Task<Guid> ParticipationIdOnOnlySegment(Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext db = NewDb();
        Guid segmentId = Assert.Single(await db.AppointmentSegments.AsNoTracking()
            .Where(s => s.AppointmentId == appointmentId).Select(s => s.Id).ToListAsync()).Value;
        return (await db.BookingSegmentParticipations.AsNoTracking()
            .SingleAsync(p => p.AppointmentSegmentId == segmentId && p.Booking.ClientId == clientId)).Id.Value;
    }

    #endregion

    #region Group helpers (through the real GroupService)

    /// <summary>Group-mode Service (60 min, price 15 by default) that <see cref="Employee"/> is authorized to teach and that is offered at <see cref="Company"/>.</summary>
    public async Task<ServiceEntity> AddGroupService(int durationMinutes = 60, decimal price = 15m)
    {
        ServiceEntity service = await AddService(durationMinutes, price, name: "Group service", mode: ServiceExecutionMode.Group);
        await AssignEmployeeToService(Employee, service);
        return service;
    }

    /// <summary>Creates a Group with ONE segment template (the service, its default duration, <paramref name="capacity"/>,
    /// the trainer as the template's only employee unless <paramref name="withTrainer"/> is false) and one weekly slot on
    /// <see cref="FutureDay"/>'s weekday (10:00 unless overridden).</summary>
    public Task<GroupDto> CreateGroup(
        ServiceEntity groupService, int capacity, Employee trainer = null, bool withTrainer = true, Room room = null,
        params (DayOfWeek Day, TimeSpan Start)[] slots)
    {
        GroupCreateRequest request = new()
        {
            Name = $"Group-{Guid.NewGuid():N}",
            CompanyId = Company.Id.Value,
            SegmentTemplates = new List<GroupSegmentTemplateRequest>
            {
                new()
                {
                    ServiceId = groupService.Id.Value,
                    StartOffsetMinutes = 0,
                    RoomId = room?.Id,
                    Capacity = capacity,
                    EmployeeIds = withTrainer ? new List<Guid> { (trainer ?? Employee).Id.Value } : new List<Guid>()
                }
            },
            Slots = (slots is { Length: > 0 } ? slots : new (DayOfWeek Day, TimeSpan Start)[] { (FutureDay.DayOfWeek, TimeSpan.FromHours(10)) })
                .Select(x => new GroupSlotCreateRequest { DayOfWeek = x.Day, StartTime = TimeOnly.FromTimeSpan(x.Start) }).ToList()
        };
        return Groups.Create(OrganizationId, ActorUserId, request);
    }

    /// <summary>M1H: full-replacement edit of the group's ONLY segment template (target
    /// <c>PUT /groups/{id}/segment-templates/{templateId}</c>), starting from its current definition.</summary>
    public Task<GroupDto> UpdateOnlyTemplate(GroupDto group, Action<GroupSegmentTemplateRequest> mutate)
    {
        GroupSegmentTemplateDto current = Assert.Single(group.SegmentTemplates);
        GroupSegmentTemplateRequest request = new()
        {
            ServiceId = current.ServiceId,
            StartOffsetMinutes = current.StartOffsetMinutes,
            DurationMinutes = current.DurationMinutes,
            RoomId = current.RoomId,
            Capacity = current.Capacity,
            Resources = current.Resources.Select(r => new GroupSegmentTemplateResourceRequest { ResourceId = r.ResourceId, QuantityRequired = r.QuantityRequired }).ToList(),
            EmployeeIds = current.Employees.Select(e => e.EmployeeId).ToList(),
            PricingMode = current.Employees.Count > 1 ? current.PricingMode : null,
            PricingEmployeeId = current.Employees.Count > 1 ? current.PricingEmployeeId : null
        };
        mutate(request);
        return Groups.UpdateSegmentTemplate(OrganizationId, ActorUserId, group.Id, current.Id, request);
    }

    /// <summary>M1H: membership with an EXPLICIT selection of every template of the group (never inferred).</summary>
    public Task<GroupDto> AddGroupMember(GroupDto group, Client client) =>
        Groups.AddMember(OrganizationId, ActorUserId, group.Id, new GroupMemberAddRequest
        {
            ClientId = client.Id.Value,
            SegmentTemplateIds = group.SegmentTemplates.Select(t => t.Id).ToList()
        });

    public Task<GenerateGroupAppointmentsResult> GenerateOccurrences(
        GroupDto group, DateTimeOffset from, DateTimeOffset? to = null, bool overrideAvailability = false) =>
        Groups.GenerateAppointments(OrganizationId, ActorUserId, new GenerateGroupAppointmentsRequest
        {
            GroupId = group.Id,
            // T1-7: raspon je DateOnly — zidni datum vrijednosti (isto kao dosadašnji FromWallDate; svijet je u UTC-u).
            FromDate = Day(from),
            ToDate = Day(to ?? from),
            OverrideAvailability = overrideAvailability
        });

    /// <summary>Generates the single occurrence on <see cref="FutureDay"/> and returns its persisted Appointment (with Bookings).</summary>
    /// <summary>P1: a no-show needs a STARTED segment (ATTENDANCE_BEFORE_START). Moves every segment of an existing
    /// (future) appointment from <see cref="FutureDay"/> to <see cref="PastDay"/>, same time of day — setup, not behaviour.</summary>
    public async Task MoveToPast(Guid appointmentId)
    {
        await using DatabaseContext db = NewDb();
        TimeSpan shift = PastDay - FutureDay;
        foreach (AppointmentSegment segment in await db.AppointmentSegments.Where(s => s.AppointmentId == appointmentId).ToListAsync())
        {
            segment.PlannedStart += shift;
            segment.PlannedEnd += shift;
        }
        await db.SaveChangesAsync();
    }

    public async Task<Appointment> GenerateSingleOccurrence(GroupDto group)
    {
        GenerateGroupAppointmentsResult result = await GenerateOccurrences(group, FutureDay);
        return await LoadAppointment(Assert.Single(result.Created).Id);
    }

    public async Task<Core.DTOs.Appointments.BookingDto> AddGuest(Appointment occurrence, Client client) =>
        await Bookings.AddGroupGuest(OrganizationId, ActorUserId, true, occurrence.Id.Value,
            new BookingCreateRequest { ClientId = client.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id });

    /// <summary>M1F: gives a user exactly these raw grants through a real GrantGroup (grant resolution is not mocked).</summary>
    public async Task GrantUser(Guid userId, params string[] grants)
    {
        await using DatabaseContext db = NewDb();
        Guid groupId = Guid.NewGuid();
        db.GrantGroups.Add(new BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions.GrantGroup
        {
            Id = groupId, OrganizationId = OrganizationId, Name = $"test-{groupId:N}", CreatedAt = TestClock.UtcNow,
            Grants = grants.Select(g => new BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions.GrantGroupGrant { GrantGroupId = groupId, GrantKey = g }).ToList()
        });
        db.UserGrantGroups.Add(new BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions.UserGrantGroup { UserId = userId, GrantGroupId = groupId });
        await db.SaveChangesAsync();
        // GrantResolver keeps a ~30 s per-user cache (accepted production trade-off); a test that grants AFTER a refused
        // check evicts it so the new grants are seen immediately (key format of GrantResolver).
        Resolve<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove($"grant-context:{OrganizationId}:{userId}");
    }

    /// <summary>M1F: a member user (no grants) of this organization.</summary>
    public async Task<Guid> AddMemberUser()
    {
        await using DatabaseContext db = NewDb();
        return await AddUser(db);
    }

    public async Task<Group> LoadGroup(Guid id)
    {
        await using DatabaseContext db = NewDb();
        return await db.Groups.AsNoTracking().Include(g => g.Members).Include(g => g.Slots).SingleAsync(g => g.Id == id);
    }

    #endregion

    #region Direct seeding of pre-existing state (bypasses the service layer on purpose — setup only)

    /// <summary>
    /// Inserts an Appointment + Bookings straight into the database. Used ONLY where a test needs a starting state the
    /// public flows cannot produce (a past appointment that is still Confirmed, a Completed appointment with terminal
    /// bookings, a start time relative to the real clock). The behaviour under test is always driven afterwards through
    /// the real services.
    /// </summary>
    public async Task<Appointment> SeedAppointment(
        DateTimeOffset startsAt, AppointmentStatus status = AppointmentStatus.Scheduled, AppointmentForm form = AppointmentForm.Individual,
        Employee employee = null, ServiceEntity service = null, Room room = null, Guid? groupId = null, Guid? groupSlotId = null,
        int? durationMinutes = null, params (Client Client, BookingStatus Status, decimal Amount)[] bookings)
    {
        await using DatabaseContext db = NewDb();
        ServiceEntity svc = service ?? Service;
        Guid appointmentId = Guid.NewGuid();
        Appointment appointment = new()
        {
            Id = appointmentId,
            OrganizationId = OrganizationId,
            Form = form,
            CompanyId = Company.Id.Value,
            Status = status,
            // M1A.1/M1C: a Cancelled appointment is an EXPLICITLY cancelled one (the production shape) — segment occupancy
            // reads the explicit marker, never the aggregate status.
            CancelledAt = status == AppointmentStatus.Cancelled ? TestClock.UtcNow : null,
            GroupId = groupId,
            GroupSlotId = groupSlotId,
            CreatedAt = TestClock.UtcNow
        };
        // D3A: the execution frame lives on the single authoritative segment (same shape production creates).
        SingleSegmentTestExtensions.AddTestSegment(appointment,
            svc.Id.Value,
            form == AppointmentForm.Group && employee == null ? null : (employee ?? Employee).Id,
            room?.Id,
            startsAt,
            durationMinutes ?? svc.DefaultDurationMinutes);
        // M1F: a group occurrence's segment is generated from a template (production shape) — the seeded occurrence of a
        // one-template group links to that template.
        if (groupId.HasValue)
            appointment.Segments[0].GroupSegmentTemplateId = await db.GroupSegmentTemplates
                .Where(t => t.GroupId == groupId.Value).Select(t => t.Id).SingleAsync();
        foreach ((Client client, BookingStatus bookingStatus, decimal amount) in bookings)
        {
            // D3B1/D3B2: lifecycle and price live on the booking's single participation on the appointment's segment.
            // Seeded state has no price-list resolution, so the resolution snapshot (BaseAmount/Source) stays NULL.
            Guid bookingId = Guid.NewGuid();
            Booking booking = new()
            {
                Id = bookingId,
                OrganizationId = OrganizationId,
                AppointmentId = appointmentId,
                ClientId = client.Id.Value,
                CreatedAt = TestClock.UtcNow
            };
            // P1: current-state metadata always matches the status (DB CHECK) — a seeded Cancelled participation is a
            // business cancellation, a seeded NoShow carries its no-show stamp.
            ParticipationStatus seededStatus = BookingParticipations.ToParticipationStatus(bookingStatus);
            booking.Participations.Add(new BookingSegmentParticipation
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                BookingId = bookingId,
                AppointmentSegmentId = appointment.Segments[0].Id.Value,
                Status = seededStatus,
                CancellationInitiator = seededStatus == ParticipationStatus.Cancelled ? CancellationInitiator.Business : null,
                CancelledAt = seededStatus == ParticipationStatus.Cancelled ? booking.CreatedAt : null,
                NoShowAt = seededStatus == ParticipationStatus.NoShow ? booking.CreatedAt : null,
                Amount = amount,
                SuggestedAmount = amount,
                CreatedAt = booking.CreatedAt
            });
            appointment.Bookings.Add(booking);
        }
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment;
    }

    #endregion

    #region Booking status helpers (the real IBookingService target commands)

    /// <summary>M1H: one-segment status helper — a GROUP occurrence goes through the (appointment, client, explicit segment)
    /// attendance path; an INDIVIDUAL appointment addresses the client's participation on its only segment.
    /// P1: a cancellation through this helper is a CLIENT cancellation unless <paramref name="initiator"/> says otherwise
    /// (the API itself has no default initiator); a Business cancellation without a reason gets a fixed test reason.</summary>
    public async Task<BookingDto> SetBookingStatus(
        Guid appointmentId, Client client, BookingStatus status, string cancellationReason = null,
        Guid? clientPackageId = null, PaymentMethod? paymentMethod = null, decimal? amount = null, bool isPaid = true,
        bool hasFullScope = true, Guid? userId = null, CancellationInitiator? initiator = null, string correctionReason = null,
        bool waivePolicyConsequence = false, string waiverReason = null)
    {
        Appointment appointment = await LoadAppointment(appointmentId);
        CancellationInitiator? effectiveInitiator = status == BookingStatus.Cancelled ? initiator ?? CancellationInitiator.Client : null;
        BookingSetStatusRequest request = new()
        {
            Status = status,
            CancellationInitiator = effectiveInitiator,
            CancellationReason = status == BookingStatus.Cancelled
                ? cancellationReason ?? (effectiveInitiator == CancellationInitiator.Business ? "business test reason" : null)
                : null,
            NoShowReason = status == BookingStatus.NoShow ? cancellationReason : null,
            ClientPackageId = clientPackageId,
            PaymentMethod = paymentMethod,
            Amount = amount,
            IsPaid = isPaid,
            CorrectionReason = correctionReason,
            WaivePolicyConsequence = waivePolicyConsequence,
            WaiverReason = waiverReason
        };
        if (appointment.Form == AppointmentForm.Group)
        {
            request.SegmentId = Assert.Single(appointment.Segments).Id;
            return await Bookings.SetStatusOnSegment(OrganizationId, userId ?? ActorUserId, hasFullScope, appointmentId, client.Id.Value, request);
        }
        Guid participationId = await ParticipationIdOnOnlySegment(appointmentId, client.Id.Value);
        return await Bookings.SetParticipationStatus(OrganizationId, userId ?? ActorUserId, hasFullScope, participationId, request);
    }

    /// <summary>
    /// Puts a Booking into "package coverage applied" state and decrements the package counter, exactly as a completion
    /// would — but WITHOUT changing the Booking status. Individual flows never leave a Confirmed Booking covered (coverage is
    /// applied only while completing), so this state can only be seeded; it is used to pin the code paths that inspect
    /// coverage on a non-Completed Booking (return-on-cancel, corrections).
    /// </summary>
    public async Task SeedCoverageApplied(Booking booking, ClientPackage package)
    {
        await using DatabaseContext db = NewDb();
        // D3B3A: "coverage applied" is an ACTIVE PackageConsumption on the booking's participation (+ the counter).
        Booking tracked = await db.Bookings.Include(b => b.Participations).ThenInclude(p => p.Segment).SingleAsync(b => b.Id == booking.Id);
        BookingSegmentParticipation participation = Assert.Single(tracked.Participations);
        ClientPackageServiceEntry entry = await db.ClientPackageServiceEntries.SingleAsync(e => e.ClientPackageId == package.Id);
        if (entry.RemainingEntries.HasValue) entry.RemainingEntries -= 1;
        db.PackageConsumptions.Add(new PackageConsumption
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, ClientPackageId = package.Id.Value,
            BookingSegmentParticipationId = participation.Id.Value, ServiceId = participation.Segment.ServiceId,
            Units = entry.RemainingEntries.HasValue ? 1 : 0, ServiceStartsAt = participation.Segment.PlannedStart,
            ServiceDate = DateOnly.FromDateTime(participation.Segment.PlannedStart.UtcDateTime), // seeded worlds are UTC organizations
            Status = PackageConsumptionStatus.Consumed, CreatedAt = TestClock.UtcNow
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// M0: ARTIFICIAL multi-participation data. Production flows still create exactly one segment and one participation per
    /// Booking (multi-segment creation is not enabled); this seeds an extra segment on the SAME appointment (same service,
    /// the default employee) plus a participation of the client's existing Booking on it — so participation-native
    /// addressing, aggregation, locking and segment-scoped scheduling can be exercised. Returns the new participation id.
    /// </summary>
    public async Task<Guid> AddArtificialSegmentParticipation(
        Guid appointmentId, Client client, DateTimeOffset plannedStart, decimal amount,
        ParticipationStatus status = ParticipationStatus.Confirmed, int durationMinutes = DefaultServiceDuration)
    {
        await using DatabaseContext db = NewDb();
        Booking booking = await db.Bookings.SingleAsync(b => b.AppointmentId == appointmentId && b.ClientId == client.Id);
        AppointmentSegment existing = await db.AppointmentSegments.AsNoTracking().FirstAsync(s => s.AppointmentId == appointmentId);

        AppointmentSegment segment = new()
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, AppointmentId = appointmentId, ServiceId = existing.ServiceId,
            PlannedStart = plannedStart, PlannedEnd = plannedStart.AddMinutes(durationMinutes), CreatedAt = TestClock.UtcNow
        };
        segment.Employees.Add(new AppointmentSegmentEmployee { AppointmentSegmentId = segment.Id.Value, EmployeeId = Employee.Id.Value });
        db.AppointmentSegments.Add(segment);

        BookingSegmentParticipation participation = new()
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, BookingId = booking.Id.Value, AppointmentSegmentId = segment.Id.Value,
            // P1: current-state metadata always matches the status (DB CHECK).
            CancellationInitiator = status == ParticipationStatus.Cancelled ? CancellationInitiator.Business : null,
            CancelledAt = status == ParticipationStatus.Cancelled ? TestClock.UtcNow : null,
            NoShowAt = status == ParticipationStatus.NoShow ? TestClock.UtcNow : null,
            Status = status, StatusVersion = 0, SuggestedAmount = amount, Amount = amount, CreatedAt = TestClock.UtcNow
        };
        db.BookingSegmentParticipations.Add(participation);
        await db.SaveChangesAsync();
        return participation.Id.Value;
    }

    /// <summary>The participations of a client's Booking (fresh, no tracking), ordered by segment start.</summary>
    public async Task<List<BookingSegmentParticipation>> LoadParticipations(Guid appointmentId, Client client)
    {
        await using DatabaseContext db = NewDb();
        return await db.BookingSegmentParticipations.AsNoTracking().Include(p => p.Segment)
            .Where(p => p.Booking.AppointmentId == appointmentId && p.Booking.ClientId == client.Id)
            .OrderBy(p => p.Segment.PlannedStart)
            .ToListAsync();
    }

    #endregion

    #region Persisted-state readers

    public async Task<Appointment> LoadAppointment(Guid id)
    {
        await using DatabaseContext db = NewDb();
        return await db.Appointments.AsNoTracking()
            .Include(a => a.Bookings)
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .AsSplitQuery()
            .SingleAsync(a => a.Id == id);
    }

    public async Task<Booking> LoadBooking(Guid appointmentId, Client client)
    {
        await using DatabaseContext db = NewDb();
        // D3B3A: with its Appointment, so the derived package view knows the appointment form.
        return await db.Bookings.AsNoTracking().Include(b => b.Appointment)
            .SingleAsync(b => b.AppointmentId == appointmentId && b.ClientId == client.Id);
    }

    public async Task<int> CountAppointments(Func<IQueryable<Appointment>, IQueryable<Appointment>> filter = null)
    {
        await using DatabaseContext db = NewDb();
        IQueryable<Appointment> q = db.Appointments.Where(a => a.OrganizationId == OrganizationId);
        return await (filter == null ? q : filter(q)).CountAsync();
    }

    /// <summary>All Payments touching the booking (any status) via CheckoutItem → PaymentAllocation, newest first.</summary>
    public async Task<List<Payment>> LoadPayments(Guid bookingId)
    {
        await using DatabaseContext db = NewDb();
        return await db.CheckoutItems.AsNoTracking()
            .Where(i => i.Participation.BookingId == bookingId) // D3B3B: service items settle the booking's participation
            .SelectMany(i => i.Allocations)
            .Select(a => a.Payment)
            .Distinct()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<CheckoutItem>> LoadCheckoutItems(Guid bookingId)
    {
        await using DatabaseContext db = NewDb();
        return await db.CheckoutItems.AsNoTracking()
            .Include(i => i.Allocations).ThenInclude(a => a.Payment)
            .Where(i => i.Participation.BookingId == bookingId) // D3B3B: service items settle the booking's participation
            .ToListAsync();
    }

    /// <summary>P1: the consequence ledger of every participation on an appointment, oldest source version first.</summary>
    public async Task<List<ParticipationPolicyConsequence>> LoadPolicyConsequences(Guid appointmentId)
    {
        await using DatabaseContext db = NewDb();
        return await db.ParticipationPolicyConsequences.AsNoTracking()
            .Where(c => c.OrganizationId == OrganizationId && c.Participation.Booking.AppointmentId == appointmentId)
            .OrderBy(c => c.SourceVersion)
            .ToListAsync();
    }

    /// <summary>P1: package consumptions of every participation on an appointment, oldest first.</summary>
    public async Task<List<PackageConsumption>> LoadPackageConsumptions(Guid appointmentId)
    {
        await using DatabaseContext db = NewDb();
        return await db.PackageConsumptions.AsNoTracking()
            .Where(c => c.OrganizationId == OrganizationId && c.Participation.Booking.AppointmentId == appointmentId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<CommissionEntry>> LoadCommissionEntries()
    {
        await using DatabaseContext db = NewDb();
        return await db.CommissionEntries.AsNoTracking()
            .Where(e => e.OrganizationId == OrganizationId)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.SourceVersion)
            .ToListAsync();
    }

    public async Task<List<AppointmentAuditLog>> LoadAuditLog(Guid appointmentId)
    {
        await using DatabaseContext db = NewDb();
        return await db.AppointmentAuditLog.AsNoTracking()
            .Where(a => a.AppointmentId == appointmentId)
            .OrderBy(a => a.ChangedAt)
            .ToListAsync();
    }

    public async Task<List<OutboxMessage>> LoadOutbox()
    {
        await using DatabaseContext db = NewDb();
        return await db.OutboxMessages.AsNoTracking()
            .Where(m => m.OrganizationId == OrganizationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Notification>> LoadNotifications()
    {
        await using DatabaseContext db = NewDb();
        return await db.Notifications.AsNoTracking().Where(n => n.OrganizationId == OrganizationId).ToListAsync();
    }

    public async Task<Checkout> LoadCheckout(Guid id)
    {
        await using DatabaseContext db = NewDb();
        return await db.Checkouts.AsNoTracking().Include(c => c.Items).Include(c => c.Payments).SingleAsync(c => c.Id == id);
    }

    /// <summary>Runs the outbox → Notification step for one message (what OutboxProcessorService does in production), in its own transaction.</summary>
    public async Task ProcessOutbox(OutboxMessage message)
    {
        Infrastructure.Outbox.IOutboxMessageHandler handler = message.Type switch
        {
            Core.Events.OutboxEventTypes.BookingCancelledV1 => Resolve<Infrastructure.Outbox.Handlers.BookingCancelledNotificationHandler>(),
            Core.Events.OutboxEventTypes.BookingNoShowV1 => Resolve<Infrastructure.Outbox.Handlers.BookingNoShowNotificationHandler>(),
            Core.Events.OutboxEventTypes.WaitlistPromotedV1 => Resolve<Infrastructure.Outbox.Handlers.WaitlistPromotedNotificationHandler>(),
            _ => throw new InvalidOperationException($"No handler for {message.Type}")
        };

        await using Infrastructure.UnitOfWork.IUnitOfWork uow = await Resolve<Infrastructure.UnitOfWork.IUnitOfWorkFactory>().Begin();
        await handler.Handle(uow, OrganizationId, message.Payload, CancellationToken.None);
        await uow.CommitAsync();
    }

    public async Task<ClientPackage> LoadClientPackage(Guid id)
    {
        await using DatabaseContext db = NewDb();
        return await db.ClientPackages.AsNoTracking().Include(p => p.ServiceEntries).SingleAsync(p => p.Id == id);
    }

    public async Task<List<WaitlistEntry>> LoadWaitlist(Guid appointmentId)
    {
        await using DatabaseContext db = NewDb();
        return await db.WaitlistEntries.AsNoTracking().Where(w => w.AppointmentId == appointmentId).OrderBy(w => w.JoinedAt).ToListAsync();
    }

    #endregion
}
