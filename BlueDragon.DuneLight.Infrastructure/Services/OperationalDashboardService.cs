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
/// logiku, samo agregira (vidi BookingFinancialsCalculator/CheckoutFinancialsCalculator/WorkingHoursCalculator
/// za sve financijske/dostupnostne izračune). Datumska granica je [dayStart, dayEnd) po kalendarskom danu, isti
/// obrazac implicitne DateTimeOffset konverzije kao AppointmentService.GetAvailableSlots/RosterEntryService
/// (nema odvojene per-organizaciju timezone — vidi spec section 30).
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
        IProductStockHandler productStockHandler)
    {
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

    public async Task<OperationalDashboardDto> GetDashboard(Guid organizationId, Guid companyId, DateTimeOffset? date)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        // Isti obrazac kao AppointmentService.GetAvailableSlots (query.Date.Date) — kalendarski dan, ne
        // uvodi novu timezone pretpostavku (vidi spec section 30/31).
        DateTimeOffset dayStart = (date ?? DateTimeOffset.UtcNow).Date;
        DateTimeOffset dayEnd = dayStart.AddDays(1);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<Appointment> appointments = await _appointmentHandler.GetForDashboard(organizationId, companyId, dayStart, dayEnd);
        List<Guid> appointmentIds = appointments.Select(a => a.Id.GetValueOrDefault()).ToList();
        List<WaitlistEntry> waiting = await _waitlistHandler.GetWaitingForAppointments(organizationId, appointmentIds);

        List<DashboardScheduleOccurrenceDto> schedule = appointments
            .Select(a => BuildOccurrence(a, waiting, now))
            .ToList();

        List<DashboardStaffMemberDto> staff = await BuildStaff(organizationId, companyId, dayStart);

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
            Date = dayStart,
            Schedule = schedule,
            Staff = staff,
            Financial = financial,
            Alerts = alerts
        };
    }

    private static DashboardScheduleOccurrenceDto BuildOccurrence(Appointment appointment, List<WaitlistEntry> waiting, DateTimeOffset now)
    {
        DashboardScheduleOccurrenceDto dto = new DashboardScheduleOccurrenceDto
        {
            AppointmentId = appointment.Id.GetValueOrDefault(),
            StartsAt = appointment.StartsAt,
            DurationMinutes = appointment.DurationMinutes,
            Status = appointment.Status,
            ServiceId = appointment.ServiceId,
            ServiceName = appointment.Service?.Name,
            EmployeeId = appointment.EmployeeId,
            EmployeeName = appointment.Employee != null ? $"{appointment.Employee.FirstName} {appointment.Employee.LastName}" : null,
            RoomId = appointment.RoomId,
            RoomName = appointment.Room?.Name,
            IsGroup = appointment.Form == AppointmentForm.Group,
            GroupId = appointment.GroupId,
            GroupName = appointment.Group?.Name
        };

        if (appointment.Form == AppointmentForm.Group)
        {
            List<Booking> bookings = appointment.Bookings;
            int confirmedCount = bookings.Count(b => b.Status == BookingStatus.Confirmed);
            int capacity = appointment.Group?.Capacity ?? 0;

            dto.GroupSummary = new DashboardGroupSummaryDto
            {
                Capacity = capacity,
                ConfirmedCount = confirmedCount,
                CompletedCount = bookings.Count(b => b.Status == BookingStatus.Completed),
                NoShowCount = bookings.Count(b => b.Status == BookingStatus.NoShow),
                CancelledCount = bookings.Count(b => b.Status == BookingStatus.Cancelled),
                WaitingCount = waiting.Count(w => w.AppointmentId == appointment.Id),
                AvailableReservationSeats = Math.Max(0, capacity - confirmedCount),
                HasUnresolvedAttendance = appointment.StartsAt <= now && bookings.Any(b => b.Status == BookingStatus.Confirmed)
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
        decimal outstanding = BookingFinancialsCalculator.CalculateOutstanding(booking);
        return new DashboardBookingSummaryDto
        {
            BookingId = booking.Id.GetValueOrDefault(),
            ClientId = booking.ClientId,
            ClientName = booking.Client != null ? $"{booking.Client.FirstName} {booking.Client.LastName}" : null,
            BookingStatus = booking.Status,
            PaidAmount = BookingFinancialsCalculator.CalculatePaidAmount(booking),
            OutstandingAmount = outstanding,
            IsPaid = outstanding <= 0m,
            PackageCovered = booking.ClientPackageId.HasValue && booking.PackageCoverageApplied && !booking.PackageCoverageReturned
        };
    }

    private async Task<DashboardFinancialDto> BuildFinancial(
        Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd, List<Appointment> appointments)
    {
        // Obveze se izvode SAMO iz Bookinga na rasporedu odabranog dana ove Company (već učitano) — Cancelled
        // isključen jer trenutna poslovna pravila ne zadržavaju novčanu obvezu nad otkazanim bookingom (vidi
        // spec section 17, Booking.IsLateCancellation je trenutno samo klasifikacijska priprema, ne naplata).
        List<Booking> obligationBookings = appointments
            .SelectMany(a => a.Bookings)
            .Where(b => b.Status != BookingStatus.Cancelled)
            .ToList();

        decimal outstandingAmount = 0m;
        int unpaidBookingCount = 0;
        foreach (Booking booking in obligationBookings)
        {
            decimal outstanding = BookingFinancialsCalculator.CalculateOutstanding(booking);
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
        List<Booking> allBookings = appointments.SelectMany(a => a.Bookings).ToList();

        List<DashboardOutOfStockProductDto> outOfStock = await BuildOutOfStockProducts(organizationId, companyId);

        return new DashboardAlertsDto
        {
            WaitingCount = waiting.Count,
            NoShowCount = allBookings.Count(b => b.Status == BookingStatus.NoShow),
            CancelledBookingCount = allBookings.Count(b => b.Status == BookingStatus.Cancelled),
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

    private async Task<List<DashboardStaffMemberDto>> BuildStaff(Guid organizationId, Guid companyId, DateTimeOffset dayStart)
    {
        // GetForCompany vraća samo trenutno aktivne zaposlenike trenutno dodijeljene ovoj Company (vidi
        // EmployeeCompany) — nema povijesne evidencije dodjele kroz vrijeme, pa je za POVIJESNI datum ovo
        // trenutno-stanje aproksimacija (vidi spec section 13/46, poznato ograničenje, prijavljeno u izvještaju).
        List<Employee> employees = await _employeeHandler.GetForCompany(organizationId, companyId);
        if (employees.Count == 0)
            return new List<DashboardStaffMemberDto>();

        List<Guid> employeeIds = employees.Select(e => e.Id.GetValueOrDefault()).ToList();
        DateTimeOffset dayEndInclusive = dayStart.AddDays(1).AddTicks(-1);

        List<WorkingHoursTemplate> employeeTemplates = await _workingHoursTemplateHandler.GetForEmployees(organizationId, employeeIds);
        WorkingHoursTemplate companyTemplate = await _workingHoursTemplateHandler.GetForCompany(organizationId, companyId);
        List<RosterEntry> rosterEntries = await _rosterEntryHandler.GetForPeriod(organizationId, employeeIds, dayStart, dayStart);
        List<CompanyHoliday> companyHolidays = await _companyHolidayHandler.GetForCompaniesInRange(
            organizationId, new List<Guid> { companyId }, dayStart, dayStart);
        List<ScheduleBreak> breaks = await _scheduleBreakHandler.GetForEmployeesInRange(organizationId, employeeIds, dayStart, dayEndInclusive);

        (List<WorkingHoursCalculator.Interval> companyIntervals, _) =
            WorkingHoursCalculator.GetEffectiveCompanyIntervals(companyTemplate, companyHolidays, dayStart);

        List<DashboardStaffMemberDto> result = new List<DashboardStaffMemberDto>();

        foreach (Employee employee in employees)
        {
            Guid employeeId = employee.Id.GetValueOrDefault();
            WorkingHoursTemplate employeeTemplate = employeeTemplates.FirstOrDefault(t => t.EmployeeId == employeeId);
            List<RosterEntry> rosterForEmployee = rosterEntries.Where(r => r.EmployeeId == employeeId).ToList();

            (List<WorkingHoursCalculator.Interval> employeeIntervals, AvailabilitySource source) =
                WorkingHoursCalculator.GetEffectiveEmployeeIntervals(employeeTemplate, rosterForEmployee, dayStart);

            List<WorkingHoursCalculator.Interval> effectiveIntervals =
                WorkingHoursCalculator.IntersectIntervals(employeeIntervals, companyIntervals);

            bool isAbsent = source == AvailabilitySource.Absence;

            List<ScheduleBreak> breaksForEmployee = breaks.Where(b => b.EmployeeId == employeeId).ToList();

            // IsWorking mora se slagati sa scheduling izvorom istine (AppointmentService.GenerateSlots tretira
            // ScheduleBreak kao busy raspon) — oduzimamo pauze od efektivnih intervala SAMO za taj izračun.
            // WorkIntervals namjerno ostaje puni planirani raspon prije pauza (vidi DTO napomenu).
            List<(TimeSpan Start, TimeSpan End)> busyFromBreaks = breaksForEmployee
                .Select(b => (b.StartsAt.TimeOfDay, b.StartsAt.TimeOfDay + TimeSpan.FromMinutes(b.DurationMinutes)))
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
                    .Select(i => new DashboardWorkIntervalDto { Start = i.Start, End = i.End })
                    .ToList(),
                Breaks = breaksForEmployee
                    .Select(b => new DashboardBreakDto { StartsAt = b.StartsAt, DurationMinutes = b.DurationMinutes })
                    .ToList()
            });
        }

        return result;
    }
}
