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
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        db.EngagementTypes.Add(new EngagementType
        {
            Id = EngagementTypeId,
            OrganizationId = OrganizationId,
            Name = "Full time",
            IsActive = true,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        ActorUserId = await AddUser(UserRole.Admin, db);

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

        // Direct SQL keyed on the world's organization. session_replication_role = replica suspends FK triggers for the
        // transaction so deletion order does not matter (the connection user is the database owner, as in every other
        // DB-backed test here). Child tables that carry no organization_id are removed through their parent first.
        await using DatabaseContext db = NewDb();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");

        Guid org = OrganizationId;
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
            "DELETE FROM dunelight.group_members WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.group_slots WHERE group_id IN (SELECT id FROM dunelight.groups WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_companies WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_services WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.employee_audit_log WHERE employee_id IN (SELECT id FROM dunelight.employees WHERE organization_id = {0})",
            "DELETE FROM dunelight.roster_audit_log WHERE roster_entry_id IN (SELECT id FROM dunelight.roster_entries WHERE organization_id = {0})",
            "DELETE FROM dunelight.service_companies WHERE service_id IN (SELECT id FROM dunelight.services WHERE organization_id = {0})",
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

    public static DateTimeOffset Future(int hour, int minute = 0) => FutureDay.AddHours(hour).AddMinutes(minute);
    public static DateTimeOffset Past(int hour, int minute = 0) => PastDay.AddHours(hour).AddMinutes(minute);

    #endregion

    #region Reference-data seeding

    private async Task<Guid> AddUser(UserRole role, DatabaseContext db)
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
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
        Guid userId = await AddUser(UserRole.Member, db);
        Guid employeeId = Guid.NewGuid();

        db.Employees.Add(new Employee
        {
            Id = employeeId,
            OrganizationId = OrganizationId,
            FirstName = name,
            LastName = "Tester",
            EmploymentStartDate = new DateTimeOffset(2019, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EngagementTypeId = EngagementTypeId,
            IsActive = isActive,
            UserId = userId,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
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
        await Checkouts.AddBookingItem(OrganizationId, ActorUserId, checkout.Id, new Core.DTOs.Checkouts.CheckoutAddBookingItemRequest { BookingId = bookingId });
        await Checkouts.RecordPayment(OrganizationId, ActorUserId, checkout.Id,
            new Core.DTOs.Checkouts.CheckoutPaymentCreateRequest { Amount = amount, Method = method });
        if (complete)
            await Checkouts.Complete(OrganizationId, ActorUserId, checkout.Id);
        return checkout.Id;
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
            CreatedAt = DateTimeOffset.UtcNow
        };
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            template.Intervals.Add(new WorkingHoursInterval
            {
                Id = Guid.NewGuid(),
                WorkingHoursTemplateId = templateId,
                CycleWeekIndex = 0,
                DayOfWeek = day,
                StartTime = start ?? WorkStart,
                EndTime = end ?? WorkEnd
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
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    public async Task<Room> AddRoom(Company company = null, bool allowConcurrent = false, bool isActive = true, int capacity = 1)
    {
        await using DatabaseContext db = NewDb();
        Room room = new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            CompanyId = (company ?? Company).Id.Value,
            Name = $"Room-{Guid.NewGuid():N}",
            Capacity = capacity,
            AllowConcurrentBookings = allowConcurrent,
            IsActive = isActive,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    /// <summary>Price-list row (Service, optionally company-specific) effective from <paramref name="validFrom"/>.</summary>
    public async Task AddPriceListItem(ServiceEntity service, decimal price, DateTimeOffset validFrom, Guid? companyId = null, DateTimeOffset? validTo = null)
    {
        await using DatabaseContext db = NewDb();
        db.PriceListItems.Add(new PriceListItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = OrganizationId,
            ServiceId = service.Id,
            CompanyId = companyId,
            Price = price,
            ValidFrom = validFrom,
            ValidTo = validTo,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
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

    public async Task SetCancellationCutoffMinutes(int minutes)
    {
        await using DatabaseContext db = NewDb();
        db.OrganizationSettings.Add(new OrganizationSettings
        {
            Id = Guid.NewGuid(), OrganizationId = OrganizationId, CancellationCutoffMinutes = minutes, CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
            CreatedAt = DateTimeOffset.UtcNow
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
    public async Task<ClientPackage> AddClientPackage(
        Client client, ServiceEntity service, int? entries, DateTimeOffset expiry,
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
            ValidityFixedDate = expiry,
            DefaultPrice = 100m,
            IsActive = true,
            SortOrder = 0,
            CreatedAt = DateTimeOffset.UtcNow
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
            PurchaseDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PaidPrice = 100m,
            EntryMode = mode,
            TotalEntryCount = mode == PackageEntryMode.SharedPool ? entries : null,
            RemainingSharedEntries = mode == PackageEntryMode.SharedPool ? entries : null,
            ValidityType = PackageValidityType.FixedDate,
            ExpiryDate = expiry,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
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

    public AppointmentCreateRequest CreateRequest(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, ServiceEntity service = null,
        Company company = null, Room room = null, decimal? amount = null, bool overrideAvailability = false, string note = null,
        params Client[] extraClients)
    {
        List<Guid> clientIds = new() { (client ?? Client).Id.Value };
        clientIds.AddRange(extraClients.Select(c => c.Id.Value));

        return new AppointmentCreateRequest
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

    public Task<AppointmentDto> CreateAppointment(AppointmentCreateRequest request, bool hasFullScope = true) =>
        Appointments.Create(OrganizationId, ActorUserId, hasFullScope, request);

    /// <summary>Convenience: individual appointment for the default Service/Employee/Company through the real Create flow.</summary>
    public Task<AppointmentDto> CreateAppointment(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, Room room = null, params Client[] extraClients) =>
        CreateAppointment(CreateRequest(startsAt, client, employee, room: room, extraClients: extraClients));

    /// <summary>"Complete" request (POST /complete, PATCH /{id}/complete): the create request plus one settlement per client.</summary>
    public AppointmentCompleteRequest CompleteRequest(
        DateTimeOffset startsAt, Client client = null, Employee employee = null, ServiceEntity service = null,
        Company company = null, Room room = null, decimal? amount = null, bool overrideAvailability = false,
        PaymentMethod? paymentMethod = null, bool isPaid = true, Guid? clientPackageId = null, decimal? settlementAmount = null,
        params AppointmentClientSettlement[] settlements)
    {
        AppointmentCreateRequest create = CreateRequest(startsAt, client, employee, service, company, room, amount, overrideAvailability);
        return new AppointmentCompleteRequest
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
                : new List<AppointmentClientSettlement>
                {
                    new()
                    {
                        ClientId = create.ClientIds[0],
                        PaymentMethod = paymentMethod,
                        IsPaid = isPaid,
                        ClientPackageId = clientPackageId,
                        Amount = settlementAmount
                    }
                }
        };
    }

    public Task<AppointmentDto> CompleteNew(AppointmentCompleteRequest request, bool hasFullScope = true) =>
        Appointments.CompleteNew(OrganizationId, ActorUserId, hasFullScope, request);

    public Task<AppointmentDto> CompleteExisting(Guid appointmentId, AppointmentCompleteRequest request, bool hasFullScope = true) =>
        Appointments.CompleteExisting(OrganizationId, ActorUserId, hasFullScope, appointmentId, request);

    public AppointmentUpdateRequest UpdateRequest(AppointmentDto current, Action<AppointmentUpdateRequest> mutate = null)
    {
        AppointmentUpdateRequest request = new()
        {
            StartsAt = current.StartsAt,
            ServiceId = current.ServiceId,
            EmployeeId = current.EmployeeId.Value,
            CompanyId = current.CompanyId,
            RoomId = current.RoomId,
            ClientIds = current.Bookings.Select(b => b.ClientId).ToList(),
            Note = current.Note
        };
        mutate?.Invoke(request);
        return request;
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

    /// <summary>Creates a Group with one weekly slot on <see cref="FutureDay"/>'s weekday (10:00 unless overridden).</summary>
    public Task<GroupDto> CreateGroup(
        ServiceEntity groupService, int capacity, Employee trainer = null, bool withTrainer = true, Room room = null,
        params (DayOfWeek Day, TimeSpan Start)[] slots)
    {
        GroupCreateRequest request = new()
        {
            Name = $"Group-{Guid.NewGuid():N}",
            ServiceId = groupService.Id.Value,
            CompanyId = Company.Id.Value,
            Capacity = capacity,
            DefaultTrainerId = withTrainer ? (trainer ?? Employee).Id : null,
            DefaultRoomId = room?.Id,
            Slots = (slots is { Length: > 0 } ? slots : new (DayOfWeek Day, TimeSpan Start)[] { (FutureDay.DayOfWeek, TimeSpan.FromHours(10)) })
                .Select(x => new GroupSlotCreateRequest { DayOfWeek = x.Day, StartTime = x.Start }).ToList()
        };
        return Groups.Create(OrganizationId, ActorUserId, request);
    }

    public Task<GroupDto> AddGroupMember(GroupDto group, Client client) =>
        Groups.AddMember(OrganizationId, ActorUserId, group.Id, new GroupMemberAddRequest { ClientId = client.Id.Value });

    public Task<GenerateGroupAppointmentsResult> GenerateOccurrences(
        GroupDto group, DateTimeOffset from, DateTimeOffset? to = null, bool overrideAvailability = false) =>
        Groups.GenerateAppointments(OrganizationId, ActorUserId, new GenerateGroupAppointmentsRequest
        {
            GroupId = group.Id,
            FromDate = from,
            ToDate = to ?? from,
            OverrideAvailability = overrideAvailability
        });

    /// <summary>Generates the single occurrence on <see cref="FutureDay"/> and returns its persisted Appointment (with Bookings).</summary>
    public async Task<Appointment> GenerateSingleOccurrence(GroupDto group)
    {
        GenerateGroupAppointmentsResult result = await GenerateOccurrences(group, FutureDay);
        return await LoadAppointment(Assert.Single(result.Created).Id);
    }

    public async Task<Core.DTOs.Appointments.BookingDto> AddGuest(Appointment occurrence, Client client) =>
        await Bookings.AddBooking(OrganizationId, ActorUserId, true, occurrence.Id.Value, new BookingCreateRequest { ClientId = client.Id.Value });

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
            GroupId = groupId,
            GroupSlotId = groupSlotId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        // D3A: the execution frame lives on the single authoritative segment (same shape production creates).
        AppointmentFrameMutator.NewSegment(appointment, new AppointmentFrame(
            svc.Id.Value,
            form == AppointmentForm.Group && employee == null ? null : (employee ?? Employee).Id,
            room?.Id,
            startsAt,
            durationMinutes ?? svc.DefaultDurationMinutes));
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
                CreatedAt = DateTimeOffset.UtcNow
            };
            booking.Participations.Add(new BookingSegmentParticipation
            {
                Id = Guid.NewGuid(),
                OrganizationId = OrganizationId,
                BookingId = bookingId,
                AppointmentSegmentId = appointment.Segments[0].Id.Value,
                Status = BookingParticipations.ToParticipationStatus(bookingStatus),
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

    #region Booking status helpers (the real IBookingService.SetStatus)

    public Task<BookingDto> SetBookingStatus(
        Guid appointmentId, Client client, BookingStatus status, string cancellationReason = null, bool returnPackageEntry = false,
        Guid? clientPackageId = null, PaymentMethod? paymentMethod = null, decimal? amount = null, bool isPaid = true,
        bool hasFullScope = true, Guid? userId = null) =>
        Bookings.SetStatus(OrganizationId, userId ?? ActorUserId, hasFullScope, appointmentId, client.Id.Value, new BookingSetStatusRequest
        {
            Status = status,
            CancellationReason = cancellationReason,
            ReturnPackageEntry = returnPackageEntry,
            ClientPackageId = clientPackageId,
            PaymentMethod = paymentMethod,
            Amount = amount,
            IsPaid = isPaid
        });

    /// <summary>
    /// Puts a Booking into "package coverage applied" state and decrements the package counter, exactly as a completion
    /// would — but WITHOUT changing the Booking status. Individual flows never leave a Confirmed Booking covered (coverage is
    /// applied only while completing), so this state can only be seeded; it is used to pin the code paths that inspect
    /// coverage on a non-Completed Booking (return-on-cancel, corrections).
    /// </summary>
    public async Task SeedCoverageApplied(Booking booking, ClientPackage package)
    {
        await using DatabaseContext db = NewDb();
        Booking tracked = await db.Bookings.SingleAsync(b => b.Id == booking.Id);
        tracked.ClientPackageId = package.Id;
        tracked.PackageCoverageApplied = true;
        ClientPackageServiceEntry entry = await db.ClientPackageServiceEntries.SingleAsync(e => e.ClientPackageId == package.Id);
        if (entry.RemainingEntries.HasValue) entry.RemainingEntries -= 1;
        await db.SaveChangesAsync();
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
        return await db.Bookings.AsNoTracking().SingleAsync(b => b.AppointmentId == appointmentId && b.ClientId == client.Id);
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
            .Where(i => i.BookingId == bookingId)
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
            .Where(i => i.BookingId == bookingId)
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
