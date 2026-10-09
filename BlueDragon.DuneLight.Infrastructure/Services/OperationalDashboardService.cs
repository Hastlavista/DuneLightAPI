using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Dashboard;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Dashboard;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Products;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi IOperationalDashboardService. Sastavlja se od već postojećih handler upita — ne uvodi novu poslovnu
/// logiku, samo agregira (vidi ParticipationSettlement/CheckoutFinancialsCalculator/WorkingHoursCalculator
/// za sve financijske/dostupnostne izračune). Dan je DateOnly (T1-7); granica je [dayStart, dayEnd) = UTC instanti lokalnih
/// ponoći u efektivnoj zoni poslovnice (ADR-0013).
/// </summary>
public class OperationalDashboardService : IOperationalDashboardService
{
    private readonly ICompanyHandler _companyHandler;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IWaitlistHandler _waitlistHandler;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IWorkingHoursTemplateHandler _workingHoursTemplateHandler;
    private readonly IRosterEntryHandler _rosterEntryHandler;
    private readonly ICompanyHolidayHandler _companyHolidayHandler;
    private readonly IScheduleBreakHandler _scheduleBreakHandler;
    private readonly IProductHandler _productHandler;
    private readonly IProductStockHandler _productStockHandler;

    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly TimeProvider _timeProvider;

    public OperationalDashboardService(
        ICompanyHandler companyHandler,
        IAppointmentHandler appointmentHandler,
        IWaitlistHandler waitlistHandler,
        ICheckoutHandler checkoutHandler,
        IEmployeeHandler employeeHandler,
        IWorkingHoursTemplateHandler workingHoursTemplateHandler,
        IRosterEntryHandler rosterEntryHandler,
        ICompanyHolidayHandler companyHolidayHandler,
        IScheduleBreakHandler scheduleBreakHandler,
        IProductHandler productHandler,
        IProductStockHandler productStockHandler,
        IOrganizationCalendarService organizationCalendarService,
        TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _organizationCalendarService = organizationCalendarService;
        _companyHandler = companyHandler;
        _appointmentHandler = appointmentHandler;
        _waitlistHandler = waitlistHandler;
        _checkoutHandler = checkoutHandler;
        _employeeHandler = employeeHandler;
        _workingHoursTemplateHandler = workingHoursTemplateHandler;
        _rosterEntryHandler = rosterEntryHandler;
        _companyHolidayHandler = companyHolidayHandler;
        _scheduleBreakHandler = scheduleBreakHandler;
        _productHandler = productHandler;
        _productStockHandler = productStockHandler;
    }

    public async Task<OperationalDashboardDto> GetDashboard(Guid organizationId, Guid companyId, DateOnly? date)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        // Kalendarski dan poslovnice (T1-7: DateOnly): zatraženi dan, inače "danas" u efektivnoj zoni poslovnice;
        // granice [dayStart, dayEnd) su UTC instanti lokalnih ponoći (isto pravilo kao AppointmentService.GetAvailableSlots).
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        DateOnly day = date ?? calendar.LocalDate(_timeProvider.GetUtcNow());
        DateTimeOffset dayStart = calendar.StartOfDay(day);
        DateTimeOffset dayEnd = calendar.StartOfDay(day.AddDays(1));
        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<Appointment> appointments = await _appointmentHandler.GetForDashboard(organizationId, companyId, dayStart, dayEnd);
        List<Guid> appointmentIds = appointments.Select(a => a.Id.GetValueOrDefault()).ToList();
        List<WaitlistEntry> waiting = await _waitlistHandler.GetWaitingForAppointments(organizationId, appointmentIds);

        List<DashboardScheduleOccurrenceDto> schedule = appointments
            .Select(a => BuildOccurrence(a, waiting, now))
            .ToList();

        List<DashboardStaffMemberDto> staff = await BuildStaff(organizationId, companyId, calendar, day, dayStart, dayEnd);

        DashboardFinancialDto financial = await BuildFinancial(organizationId, companyId, dayStart, dayEnd, appointments);
        DashboardAlertsDto alerts = await BuildAlerts(organizationId, companyId, appointments, waiting, financial.UnpaidBookingCount);

        return new OperationalDashboardDto
        {
            Company = new DashboardCompanyDto
            {
                Id = company.Id.GetValueOrDefault(),
                Name = company.Name,
                IsActive = company.IsActive
            },
            Date = day,
            Schedule = schedule,
            Staff = staff,
            Financial = financial,
            Alerts = alerts
        };
    }

    private static DashboardScheduleOccurrenceDto BuildOccurrence(Appointment appointment, List<WaitlistEntry> waiting, DateTimeOffset now)
    {
        AppointmentRange range = AppointmentRange.Of(appointment);
        DashboardScheduleOccurrenceDto dto = new DashboardScheduleOccurrenceDto
        {
            AppointmentId = appointment.Id.GetValueOrDefault(),
            PlannedStart = range.PlannedStart,
            PlannedEnd = range.PlannedEnd,
            Segments = AppointmentSegmentReadModel.ToDtos(appointment),
            Status = appointment.Status,
            IsGroup = appointment.Form == AppointmentForm.Group,
            GroupId = appointment.GroupId,
            GroupName = appointment.Group?.Name
        };

        if (appointment.Form == AppointmentForm.Group)
        {
            // Phase M0: grupni sažetak broji SUDJELOVANJA (izvršne jedinice) termina, ne Bookinge.
            List<BookingSegmentParticipation> participations = appointment.Bookings.SelectMany(b => b.Participations).ToList();
            int confirmedCount = participations.Count(p => p.Status == ParticipationStatus.Confirmed);
            // Phase M1F: meki kapacitet je po segmentu (predlošku); sažetak occurrencea zbraja kapacitete segmenata (za
            // jednosegmentni occurrence = kapacitet jedinog predloška, kao prije).
            int capacity = appointment.Segments.Sum(s => s.GroupSegmentTemplate?.Capacity ?? 0);

            dto.GroupSummary = new DashboardGroupSummaryDto
            {
                Capacity = capacity,
                ConfirmedCount = confirmedCount,
                CompletedCount = participations.Count(p => p.Status == ParticipationStatus.Completed),
                NoShowCount = participations.Count(p => p.Status == ParticipationStatus.NoShow),
                CancelledCount = participations.Count(p => p.Status == ParticipationStatus.Cancelled),
                WaitingCount = waiting.Count(w => w.AppointmentId == appointment.Id),
                AvailableReservationSeats = Math.Max(0, capacity - confirmedCount),
                HasUnresolvedAttendance = range.PlannedStart <= now && confirmedCount > 0
            };
        }
        else
        {
            dto.Bookings = appointment.Bookings.Select(BuildBookingSummary).ToList();
        }

        return dto;
    }

    private static DashboardBookingSummaryDto BuildBookingSummary(Booking booking)
    {
        BookingCommercialSummary commercial = BookingCommercialSummary.Of(booking);
        return new DashboardBookingSummaryDto
        {
            BookingId = booking.Id.GetValueOrDefault(),
            ClientId = booking.ClientId,
            ClientName = booking.Client != null ? $"{booking.Client.FirstName} {booking.Client.LastName}" : null,
            BookingStatus = BookingSummary.StatusOf(booking),
            PaidAmount = commercial.MonetarySettled,
            OutstandingAmount = commercial.Outstanding,
            SurplusAmount = commercial.Surplus,
            IsPaid = commercial.FullySettled,
            // Phase M0: "pokriveno paketom" = SVA sudjelovanja Bookinga namirena paketom.
            PackageCovered = commercial.PackageCoveredCount == commercial.ParticipationCount
        };
    }

    private async Task<DashboardFinancialDto> BuildFinancial(
        Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd, List<Appointment> appointments)
    {
        // Obveze se izvode SAMO iz Bookinga na rasporedu odabranog dana ove Company (već učitano), po SUDJELOVANJU i
        // isključivo iz jedine status-aware derivacije (P1, D5 — nema lokalnog filtra po statusu: otkazano/izostalo
        // sudjelovanje duguje samo aktivnu naknadu politike). Zbraja se samo POZITIVAN dug (odluka 2026-10-06): preplata
        // jednog sudjelovanja ne umanjuje tuđi dug. "Neplaćen Booking" = Booking s barem jednim sudjelovanjem koje duguje.
        decimal outstandingAmount = 0m;
        int unpaidBookingCount = 0;
        foreach (Booking booking in appointments.SelectMany(a => a.Bookings))
        {
            decimal outstanding = booking.Participations
                .Sum(p => Math.Max(ParticipationSettlement.Of(p).OutstandingAmount, 0m));
            outstandingAmount += outstanding;
            if (outstanding > 0m)
                unpaidBookingCount++;
        }

        decimal todayRevenue = await _checkoutHandler.GetCompletedPaymentAmountForCompanyOnDate(organizationId, companyId, dayStart, dayEnd);

        List<Checkout> openCheckouts = await _checkoutHandler.GetOpenByCompany(organizationId, companyId);
        decimal openOutstanding = openCheckouts.Sum(c => CheckoutFinancialsCalculator.Calculate(c).OutstandingAmount);

        return new DashboardFinancialDto
        {
            TodayRevenue = todayRevenue,
            OutstandingAmount = outstandingAmount,
            UnpaidBookingCount = unpaidBookingCount,
            OpenCheckoutCount = openCheckouts.Count,
            OpenCheckoutOutstandingAmount = openOutstanding
        };
    }

    private async Task<DashboardAlertsDto> BuildAlerts(
        Guid organizationId, Guid companyId, List<Appointment> appointments, List<WaitlistEntry> waiting, int unpaidBookingCount)
    {
        List<BookingSegmentParticipation> allParticipations = appointments.SelectMany(a => a.Bookings).SelectMany(b => b.Participations).ToList();

        List<DashboardOutOfStockProductDto> outOfStock = await BuildOutOfStockProducts(organizationId, companyId);

        return new DashboardAlertsDto
        {
            WaitingCount = waiting.Count,
            NoShowCount = allParticipations.Count(p => p.Status == ParticipationStatus.NoShow),
            CancelledBookingCount = allParticipations.Count(p => p.Status == ParticipationStatus.Cancelled),
            CancelledAppointmentCount = appointments.Count(a => a.Status == AppointmentStatus.Cancelled),
            UnpaidBookingCount = unpaidBookingCount,
            OutOfStockCount = outOfStock.Count,
            OutOfStockProducts = outOfStock
        };
    }

    /// <summary>Efektivna količina = ProductStock.Quantity kad redak postoji za ovu Company, inače 0 — Product
    /// bez ijednog ProductStock retka se još ne može uspješno prodati (vidi finding #1). GetByCompany vraća
    /// SAMO postojeće retke, pa se aktivni katalog uzima odvojeno (GetActive) i korelira u memoriji preko
    /// Dictionary (dva bounded upita, bez N+1, bez stvaranja ProductStock redaka — čisto čitanje).</summary>
    private async Task<List<DashboardOutOfStockProductDto>> BuildOutOfStockProducts(Guid organizationId, Guid companyId)
    {
        List<Product> activeProducts = await _productHandler.GetActive(organizationId);
        if (activeProducts.Count == 0)
            return new List<DashboardOutOfStockProductDto>();

        List<ProductStock> stocks = await _productStockHandler.GetByCompany(organizationId, companyId);
        Dictionary<Guid, int> quantityByProduct = stocks.ToDictionary(s => s.ProductId, s => s.Quantity);

        return activeProducts
            .Where(p => !quantityByProduct.TryGetValue(p.Id.GetValueOrDefault(), out int quantity) || quantity == 0)
            .Select(p => new DashboardOutOfStockProductDto { ProductId = p.Id.GetValueOrDefault(), ProductName = p.Name })
            .ToList();
    }

    private async Task<List<DashboardStaffMemberDto>> BuildStaff(
        Guid organizationId, Guid companyId, OrganizationCalendar calendar, DateOnly day, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        // GetForCompany vraća samo trenutno aktivne zaposlenike trenutno dodijeljene ovoj Company (vidi
        // EmployeeCompany) — nema povijesne evidencije dodjele kroz vrijeme, pa je za POVIJESNI datum ovo
        // trenutno-stanje aproksimacija (vidi spec section 13/46, poznato ograničenje, prijavljeno u izvještaju).
        List<Employee> employees = await _employeeHandler.GetForCompany(organizationId, companyId);
        if (employees.Count == 0)
            return new List<DashboardStaffMemberDto>();

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();
        DateTimeOffset dayEndInclusive = dayEnd.AddTicks(-1);

        List<WorkingHoursTemplate> employeeTemplates = await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        List<RosterEntry> rosterEntries = await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, day, day);
        List<CompanyHoliday> companyHolidays = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, day, day);
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEndInclusive);

        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidays, day);

        List<DashboardStaffMemberDto> result = new List<DashboardStaffMemberDto>();

        foreach (Employee employee in employees)
        {
            Guid employeeId = employee.Id.GetValueOrDefault();
            WorkingHoursTemplate employeeTemplate = employeeTemplates.FirstOrDefault(t => t.EmployeeId == employeeId);
            List<RosterEntry> rosterForEmployee = rosterEntries.Where(r => r.EmployeeId == employeeId).ToList();

            (List<WorkingHoursCalculator.Interval> employeeIntervals, AvailabilitySource source) =
                WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterForEmployee, day);

            List<WorkingHoursCalculator.Interval> effectiveIntervals =
                WorkingHoursCalculator.IntersectIntervals(employeeIntervals, companyIntervals);

            bool isAbsent = source == AvailabilitySource.Absence;

            List<ScheduleBreak> breaksForEmployee = breaks.Where(b => b.EmployeeId == employeeId).ToList();

            // IsWorking mora se slagati sa scheduling izvorom istine (AppointmentService.GenerateSlots tretira
            // ScheduleBreak kao busy raspon) — oduzimamo pauze od efektivnih intervala SAMO za taj izračun.
            // WorkIntervals namjerno ostaje puni planirani raspon prije pauza (vidi DTO napomenu).
            List<(TimeSpan Start, TimeSpan End)> busyFromBreaks = breaksForEmployee
                .Select(b => (calendar.LocalTimeOfDay(b.StartsAt), calendar.LocalTimeOfDay(b.StartsAt) + TimeSpan.FromMinutes(b.DurationMinutes)))
                .ToList();
            List<WorkingHoursCalculator.Interval> postBreakIntervals =
                WorkingHoursCalculator.SubtractIntervals(effectiveIntervals, busyFromBreaks);

            result.Add(new DashboardStaffMemberDto
            {
                EmployeeId = employeeId,
                EmployeeName = $"{employee.FirstName} {employee.LastName}",
                IsActive = employee.IsActive,
                IsAbsent = isAbsent,
                IsWorking = !isAbsent && postBreakIntervals.Count > 0,
                WorkIntervals = effectiveIntervals
                    .Select(i => new DashboardWorkIntervalDto { Start = WorkingHoursCalculator.ToTimeOnly(i.Start), End = WorkingHoursCalculator.ToTimeOnly(i.End) })
                    .ToList(),
                Breaks = breaksForEmployee
                    .Select(b => new DashboardBreakDto { StartsAt = b.StartsAt, DurationMinutes = b.DurationMinutes })
                    .ToList()
            });
        }

        return result;
    }
}
