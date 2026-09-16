using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AppointmentHandler : IAppointmentHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public AppointmentHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    /// <summary>Booking statusi koji "zauzimaju" klijentov raspored — Cancelled/NoShow namjerno isključeni
    /// (klijent koji je otkazao/izostao nije stvarno spriječen zakazati nešto drugo u to vrijeme).</summary>
    private static bool IsActiveBookingStatus(BookingStatus status) => status != BookingStatus.Cancelled && status != BookingStatus.NoShow;

    private static IQueryable<Appointment> IncludeGraph(IQueryable<Appointment> query)
    {
        return query
            .Include(a => a.Service)
            .Include(a => a.Employee)
            .Include(a => a.Company)
            .Include(a => a.Room)
            .Include(a => a.Bookings).ThenInclude(b => b.Client);
    }

    public async Task Add(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
    }

    public async Task Add(IUnitOfWork uow, Appointment appointment)
    {
        uow.Context.Appointments.Add(appointment);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<Appointment> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Appointments)
            .Include(a => a.Bookings).ThenInclude(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Appointment> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Appointment> GetWithBookingsForMutation(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Include(a => a.Service)
            .Include(a => a.Employee)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.Client)
            .Include(a => a.Bookings).ThenInclude(b => b.Client)
            .Include(a => a.Bookings).ThenInclude(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Booking> GetBooking(Guid organizationId, Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings
            .Include(b => b.Client)
            .Include(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.AppointmentId == appointmentId && b.ClientId == clientId && b.OrganizationId == organizationId);
    }

    public Task<Booking> GetBooking(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid clientId)
    {
        return uow.Context.Bookings
            .Include(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.AppointmentId == appointmentId && b.ClientId == clientId && b.OrganizationId == organizationId);
    }

    public async Task<Booking> GetBookingById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings
            .Include(b => b.Appointment).ThenInclude(a => a.Service)
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id);
    }

    public Task<Booking> GetBookingById(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return uow.Context.Bookings
            .Include(b => b.Appointment).ThenInclude(a => a.Service)
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == id);
    }

    public Task<List<Booking>> GetBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId, List<Guid> clientIds)
    {
        return uow.Context.Bookings
            .Where(b => b.AppointmentId == appointmentId && b.OrganizationId == organizationId && clientIds.Contains(b.ClientId))
            .ToListAsync();
    }

    public async Task AddBooking(IUnitOfWork uow, Booking booking)
    {
        uow.Context.Bookings.Add(booking);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateBooking(Booking booking)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Bookings.Update(booking);
        await context.SaveChangesAsync();
    }

    public async Task UpdateBooking(IUnitOfWork uow, Booking booking)
    {
        uow.Context.Bookings.Update(booking);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateScalar(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Update(appointment);
        await context.SaveChangesAsync();
    }

    public async Task UpdateScalar(IUnitOfWork uow, Appointment appointment)
    {
        uow.Context.Appointments.Update(appointment);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateWithBookings(
        Appointment appointment, List<Guid> clientIds, decimal amount = 0, decimal suggestedAmount = 0, bool overridden = false)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        await UpdateWithBookingsCore(context, appointment, clientIds, amount, suggestedAmount, overridden);
        await context.SaveChangesAsync();
    }

    public async Task UpdateWithBookings(
        IUnitOfWork uow, Appointment appointment, List<Guid> clientIds,
        decimal amount = 0, decimal suggestedAmount = 0, bool overridden = false)
    {
        await UpdateWithBookingsCore(uow.Context, appointment, clientIds, amount, suggestedAmount, overridden);
        await uow.Context.SaveChangesAsync();
    }

    private static bool IsTerminalBookingStatus(BookingStatus status) =>
        status == BookingStatus.Completed || status == BookingStatus.Cancelled || status == BookingStatus.NoShow;

    private static async Task UpdateWithBookingsCore(
        DatabaseContext context, Appointment appointment, List<Guid> clientIds,
        decimal amount, decimal suggestedAmount, bool overridden)
    {
        List<Booking> existing = await context.Bookings
            .Where(b => b.AppointmentId == appointment.Id)
            .ToListAsync();

        List<Booking> toRemove = existing.Where(b => !clientIds.Contains(b.ClientId)).ToList();
        context.Bookings.RemoveRange(toRemove);

        // Re-cijenjenje se primjenjuje samo na preživjele retke koji NISU terminalni — već naplaćen/otkazan/
        // izostao Booking čuva svoj povijesni Amount (vidi spec section 18/20).
        foreach (Booking survivor in existing.Where(b => clientIds.Contains(b.ClientId) && !IsTerminalBookingStatus(b.Status)))
        {
            survivor.Amount = amount;
            survivor.SuggestedAmount = suggestedAmount;
            survivor.IsAmountManuallyOverridden = overridden;
        }

        List<Guid> existingClientIds = existing.Select(b => b.ClientId).ToList();
        foreach (Guid clientId in clientIds.Where(id => !existingClientIds.Contains(id)))
        {
            context.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                OrganizationId = appointment.OrganizationId,
                AppointmentId = appointment.Id.GetValueOrDefault(),
                ClientId = clientId,
                Status = BookingStatus.Confirmed,
                Amount = amount,
                SuggestedAmount = suggestedAmount,
                IsAmountManuallyOverridden = overridden,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        context.Appointments.Update(appointment);
    }

    public async Task Delete(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.Remove(appointment);
        await context.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<Appointment> candidates = await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                (excludeId == null || a.Id != excludeId))
            .ToListAsync();

        return candidates.Where(a => a.StartsAt < newEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes)).ToList();
    }

    public async Task<List<Appointment>> GetOverlappingForClients(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<Appointment> candidates = await context.Appointments
            .Include(a => a.Bookings)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                a.Bookings.Any(b => clientIds.Contains(b.ClientId) && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow) &&
                (excludeId == null || a.Id != excludeId))
            .ToListAsync();

        return candidates.Where(a => a.StartsAt < newEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes)).ToList();
    }

    public async Task<List<Appointment>> GetOverlappingForRoom(
        Guid organizationId, Guid roomId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId)
    {
        DateTimeOffset newEnd = startsAt.AddMinutes(durationMinutes);
        DateTimeOffset windowStart = startsAt.AddDays(-1);
        DateTimeOffset windowEnd = startsAt.AddDays(1);

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        List<Appointment> candidates = await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.RoomId == roomId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= windowStart && a.StartsAt <= windowEnd &&
                (excludeId == null || a.Id != excludeId))
            .ToListAsync();

        return candidates.Where(a => a.StartsAt < newEnd && startsAt < a.StartsAt.AddMinutes(a.DurationMinutes)).ToList();
    }

    public async Task<List<Appointment>> GetForEmployeeInRange(
        Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForRoomInRange(
        Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.RoomId == roomId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForEmployeesInRange(
        Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.EmployeeId != null && employeeIds.Contains(a.EmployeeId.Value) &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo)
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForClientsInRange(
        Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Appointments
            .Include(a => a.Bookings)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.StartsAt >= rangeFrom && a.StartsAt <= rangeTo &&
                a.Bookings.Any(b => clientIds.Contains(b.ClientId) && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow))
            .ToListAsync();
    }

    public async Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> q = IncludeGraph(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive))
            .AsSplitQuery()
            .Where(a => a.OrganizationId == organizationId && a.StartsAt >= query.From && a.StartsAt <= query.To);

        if (query.CompanyId.HasValue)
            q = q.Where(a => a.CompanyId == query.CompanyId.Value);

        if (query.RoomId.HasValue)
            q = q.Where(a => a.RoomId == query.RoomId.Value);

        if (query.EmployeeId.HasValue)
            q = q.Where(a => a.EmployeeId == query.EmployeeId.Value);

        if (query.ServiceId.HasValue)
            q = q.Where(a => a.ServiceId == query.ServiceId.Value);

        if (query.ExecutionMode.HasValue)
            q = q.Where(a => a.Service.ExecutionMode == query.ExecutionMode.Value);

        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status.Value);

        return await q.OrderBy(a => a.StartsAt).ToListAsync();
    }

    public async Task<List<Appointment>> GetForDashboard(Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive))
            .Include(a => a.Bookings).ThenInclude(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .Where(a => a.OrganizationId == organizationId && a.CompanyId == companyId &&
                a.StartsAt >= dayStart && a.StartsAt < dayEnd)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
            .ToListAsync();
    }

    /// <summary>Termini na kojima klijent ima BILO KOJI Booking redak (bilo kojeg statusa — povijest uključuje i
    /// Cancelled/NoShow, isto kao prije uvođenja Bookinga).</summary>
    public async Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> query = IncludeGraph(context.Appointments)
            .Include(a => a.Group)
            .Include(a => a.Bookings).ThenInclude(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .Where(a => a.OrganizationId == organizationId && a.Bookings.Any(b => b.ClientId == clientId));

        int totalCount = await query.CountAsync();

        List<Appointment> items = await query
            .OrderByDescending(a => a.StartsAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<(List<Appointment> Items, int TotalCount)> GetByEmployee(Guid organizationId, Guid employeeId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> query = IncludeGraph(context.Appointments)
            .Include(a => a.Group)
            .Include(a => a.Bookings).ThenInclude(b => b.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .Where(a => a.OrganizationId == organizationId &&
                a.EmployeeId == employeeId &&
                a.Status == AppointmentStatus.Completed);

        int totalCount = await query.CountAsync();

        List<Appointment> items = await query
            .OrderByDescending(a => a.StartsAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public Task<List<Appointment>> GetFutureScheduledForGroup(IUnitOfWork uow, Guid organizationId, Guid groupId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return uow.Context.Appointments
            .Include(a => a.Bookings)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.GroupId == groupId &&
                a.Status == AppointmentStatus.Scheduled &&
                a.StartsAt >= now)
            .ToListAsync();
    }

    public async Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await context.Appointments.AnyAsync(a =>
            a.OrganizationId == organizationId &&
            a.EmployeeId == employeeId &&
            a.Status == AppointmentStatus.Scheduled &&
            a.StartsAt >= now);
    }

    public async Task<bool> HasAnyForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings.AnyAsync(b =>
            b.ClientId == clientId &&
            b.OrganizationId == organizationId);
    }

    /// <summary>Ima li klijent ijedan budući termin statusa Scheduled s aktivnim (Confirmed) Bookingom — koristi
    /// ClientService.Anonymize da blokira anonimizaciju dok postoji operativno aktivna rezervacija. Otkazan/
    /// odrađen/izostao ne blokira.</summary>
    public async Task<bool> HasFutureScheduledForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return await context.Bookings.AnyAsync(b =>
            b.ClientId == clientId &&
            b.OrganizationId == organizationId &&
            b.Status == BookingStatus.Confirmed &&
            context.Appointments.Any(a =>
                a.Id == b.AppointmentId &&
                a.Status == AppointmentStatus.Scheduled &&
                a.StartsAt >= now));
    }

    /// <summary>FOR UPDATE preko FromSqlInterpolated (parametrizirano, sigurno od SQL injection) — Postgres Read
    /// Committed onda blokira konkurentni poziv nad ISTIM terminom dok se ova transakcija ne commita/rollbacka,
    /// nakon čega konkurentni poziv čita već-commitano stanje (svježi SELECT po statementu). Include(a => a.Group)
    /// se komponira nad FromSql rezultatom (podržano u EF Core/Npgsql).</summary>
    public async Task<Appointment> GetForUpdateWithGroup(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Group)
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetForUpdateWithBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Bookings)
            .SingleOrDefaultAsync();
    }

    public async Task<int> CountConfirmedBookings(Guid organizationId, Guid appointmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings.CountAsync(b =>
            b.OrganizationId == organizationId && b.AppointmentId == appointmentId && b.Status == BookingStatus.Confirmed);
    }

    public Task<int> CountConfirmedBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return uow.Context.Bookings.CountAsync(b =>
            b.OrganizationId == organizationId && b.AppointmentId == appointmentId && b.Status == BookingStatus.Confirmed);
    }

    public async Task<Dictionary<Guid, int>> GetNoShowCountsByClientIds(Guid organizationId, List<Guid> clientIds)
    {
        if (clientIds.Count == 0)
            return new Dictionary<Guid, int>();

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings
            .Where(b =>
                clientIds.Contains(b.ClientId) &&
                b.OrganizationId == organizationId &&
                b.Status == BookingStatus.NoShow)
            .GroupBy(b => b.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClientId, g => g.Count);
    }

    public async Task AddRange(List<Appointment> appointments)
    {
        if (appointments.Count == 0)
            return;

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Appointments.AddRange(appointments);
        await context.SaveChangesAsync();
    }

    /// <summary>Jedan upit nad Booking — individualni i grupni termini dijele istu tablicu, pa više nema potrebe
    /// za odvojenim AppointmentClient/AppointmentAttendance granama (vidi Booking.cs).</summary>
    public async Task<ClientAppointmentStatsDto> GetStatsForClient(Guid organizationId, Guid clientId, List<Guid> activeGroupIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IQueryable<Booking> bookings = context.Bookings
            .Where(b => b.ClientId == clientId && b.OrganizationId == organizationId);

        int completed = await bookings.CountAsync(b => b.Status == BookingStatus.Completed);
        int noShow = await bookings.CountAsync(b => b.Status == BookingStatus.NoShow);
        int cancelled = await bookings.CountAsync(b => b.Status == BookingStatus.Cancelled);

        DateTimeOffset? lastVisit = await bookings
            .Where(b => b.Status == BookingStatus.Completed)
            .Select(b => (DateTimeOffset?)b.Appointment.StartsAt)
            .MaxAsync();

        DateTimeOffset? nextVisit = await bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.Appointment.Status == AppointmentStatus.Scheduled && b.Appointment.StartsAt > now)
            .Select(b => (DateTimeOffset?)b.Appointment.StartsAt)
            .MinAsync();

        return new ClientAppointmentStatsDto
        {
            CompletedVisitsCount = completed,
            NoShowCount = noShow,
            CancelledCount = cancelled,
            LastVisitAt = lastVisit,
            NextVisitAt = nextVisit
        };
    }
}
