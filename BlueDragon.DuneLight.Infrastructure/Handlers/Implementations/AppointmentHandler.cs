using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AppointmentHandler : IAppointmentHandler
{
    private readonly DatabaseSettings _databaseSettings;
    private readonly TimeProvider _timeProvider;

    public AppointmentHandler(DatabaseSettings databaseSettings, TimeProvider timeProvider)
    {
        _databaseSettings = databaseSettings;
        _timeProvider = timeProvider;
    }

    /// <summary>Booking statusi koji "zauzimaju" klijentov raspored — Cancelled/NoShow namjerno isključeni
    /// (klijent koji je otkazao/izostao nije stvarno spriječen zakazati nešto drugo u to vrijeme).</summary>
    private static bool IsActiveBookingStatus(BookingStatus status) => status != BookingStatus.Cancelled && status != BookingStatus.NoShow;

    /// <summary>Phase D3A: izvršni okvir (usluga, prostorija, zaposlenik) se učitava preko segmenata.</summary>
    private static IQueryable<Appointment> IncludeGraph(IQueryable<Appointment> query)
    {
        return query
            .Include(a => a.Segments).ThenInclude(s => s.Service)
            .Include(a => a.Segments).ThenInclude(s => s.Room)
            .Include(a => a.Segments).ThenInclude(s => s.Employees).ThenInclude(e => e.Employee)
            .Include(a => a.Segments).ThenInclude(s => s.Resources).ThenInclude(r => r.Resource)
            .Include(a => a.Company)
            .Include(a => a.Bookings).ThenInclude(b => b.Client);
    }

    /// <summary>Samo segment(i) + dodjele zaposlenika, bez kataloških navigacija — za čitanje okvira/vlasništva i za
    /// izmjenu (navigacije se ne učitavaju da ne bi kod spremanja nadjačale promijenjeni FK).</summary>
    private static IQueryable<Appointment> IncludeFrame(IQueryable<Appointment> query)
    {
        return query.Include(a => a.Segments).ThenInclude(s => s.Employees);
    }

    /// <summary>
    /// Phase D3A — priprema termina (i njegovog segmenta) za spremanje. Praćen entitet (FOR UPDATE unutar UnitOfWork):
    /// EF već prati izmjene segmenta i dodjela (uklonjena dodjela = orphan brisanje, nova = Added) — samo DetectChanges.
    /// Nepraćen graf: Update(graph) za termin/segment, a dodjele zaposlenika (složeni ključ) se sinkroniziraju eksplicitno
    /// — Update(graph) bi novu dodjelu označio kao Modified, a uklonjenu uopće ne bi obrisao.
    /// </summary>
    private static async Task PrepareForSave(DatabaseContext context, Appointment appointment)
    {
        if (context.Entry(appointment).State != EntityState.Detached)
        {
            context.ChangeTracker.DetectChanges();
            return;
        }

        List<(AppointmentSegment Segment, List<Guid> EmployeeIds)> desired = appointment.Segments
            .Select(s => (s, s.Employees.Select(e => e.EmployeeId).ToList()))
            .ToList();
        // Sudjelovanja se nikad ne spremaju kroz graf segmenta (životni ciklus se piše preko Bookinga — D3B1); EF ih je
        // kod učitavanja povezao i na segment, pa bi ih Update(graph) pokušao pratiti drugi put.
        foreach ((AppointmentSegment segment, _) in desired)
        {
            segment.Employees = new List<AppointmentSegmentEmployee>();
            segment.Participations = new List<BookingSegmentParticipation>();
        }

        context.Appointments.Update(appointment);

        foreach ((AppointmentSegment segment, List<Guid> employeeIds) in desired)
        {
            List<AppointmentSegmentEmployee> existing = await context.AppointmentSegmentEmployees
                .Where(e => e.AppointmentSegmentId == segment.Id)
                .ToListAsync();
            context.AppointmentSegmentEmployees.RemoveRange(existing.Where(e => !employeeIds.Contains(e.EmployeeId)));
            foreach (Guid employeeId in employeeIds.Where(id => existing.All(e => e.EmployeeId != id)))
                context.AppointmentSegmentEmployees.Add(new AppointmentSegmentEmployee
                {
                    AppointmentSegmentId = segment.Id.GetValueOrDefault(),
                    EmployeeId = employeeId
                });
        }
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
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Appointment> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeFrame(context.Appointments)
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Appointment> GetWithBookingsForMutation(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeFrame(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive)).ThenInclude(m => m.Client)
            .Include(a => a.Bookings).ThenInclude(b => b.Client)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == id);
    }

    public async Task<Booking> GetBooking(Guid organizationId, Guid appointmentId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Bookings
            .Include(b => b.Client)
            .Include(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.AppointmentId == appointmentId && b.ClientId == clientId && b.OrganizationId == organizationId);
    }

    public Task<Booking> GetBooking(IUnitOfWork uow, Guid organizationId, Guid appointmentId, Guid clientId)
    {
        return uow.Context.Bookings
            .Include(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.AppointmentId == appointmentId && b.ClientId == clientId && b.OrganizationId == organizationId);
    }

    public Task<Booking> GetBookingById(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return uow.Context.Bookings
            .Include(b => b.Appointment).ThenInclude(a => a.Segments).ThenInclude(s => s.Service)
            .Include(b => b.Appointment).ThenInclude(a => a.Segments).ThenInclude(s => s.Employees)
            .AsSplitQuery()
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
        // Booking učitan u ovoj transakciji je već praćen: change tracker spremi samo stvarno izmijenjene retke. Update() bi
        // označio CIJELI graf (sudjelovanja, potrošnje paketa, posljedice politike) kao izmijenjen i ponovno zapisao
        // nepromjenjive ledger retke (P1). Update() ostaje samo za nepraćen (odvojen) Booking.
        if (uow.Context.Entry(booking).State == EntityState.Detached)
            uow.Context.Bookings.Update(booking);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateScalar(Appointment appointment)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        await PrepareForSave(context, appointment);
        await context.SaveChangesAsync();
    }

    public async Task UpdateScalar(IUnitOfWork uow, Appointment appointment)
    {
        await PrepareForSave(uow.Context, appointment);
        await uow.Context.SaveChangesAsync();
    }

    /// <summary>Phase D3B1: termin se fizički briše samo ako je SVAKO sudjelovanje svih njegovih Bookinga netaknuto —
    /// sudjelovanja i Bookinzi se brišu eksplicitno (ParticipationHistory), zatim termin (segmenti kaskadom u bazi).
    /// Bilo koje sudjelovanje s poviješću → REFERENCED_CANNOT_DELETE i ništa se ne briše.</summary>
    public async Task Delete(IUnitOfWork uow, Appointment appointment)
    {
        DatabaseContext context = uow.Context;
        Appointment tracked = await context.Appointments
            .Include(a => a.Bookings)
            .SingleAsync(a => a.Id == appointment.Id && a.OrganizationId == appointment.OrganizationId);

        await ParticipationHistory.RemoveUntouched(context, tracked.Bookings,
            "Termin ima povijest sudjelovanja i ne može se trajno obrisati — otkažite ga umjesto toga.");
        context.Appointments.Remove(tracked);
        await context.SaveChangesAsync();
    }

    public async Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> q = IncludeGraph(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive))
            .AsSplitQuery()
            .Where(a => a.OrganizationId == organizationId &&
                a.Segments.Any(s => s.PlannedStart >= query.From && s.PlannedStart <= query.To));

        if (query.CompanyId.HasValue)
            q = q.Where(a => a.CompanyId == query.CompanyId.Value);

        if (query.RoomId.HasValue)
            q = q.Where(a => a.Segments.Any(s => s.RoomId == query.RoomId.Value));

        if (query.EmployeeId.HasValue)
            q = q.Where(a => a.Segments.Any(s => s.Employees.Any(e => e.EmployeeId == query.EmployeeId.Value)));

        if (query.ServiceId.HasValue)
            q = q.Where(a => a.Segments.Any(s => s.ServiceId == query.ServiceId.Value));

        if (query.ExecutionMode.HasValue)
            q = q.Where(a => a.Segments.Any(s => s.Service.ExecutionMode == query.ExecutionMode.Value));

        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status.Value);

        return await q.OrderBy(a => a.Segments.Min(s => s.PlannedStart)).ToListAsync();
    }

    public async Task<List<Appointment>> GetForDashboard(Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Appointments)
            .Include(a => a.Group).ThenInclude(g => g.Members.Where(m => m.IsActive))
            .Include(a => a.Segments).ThenInclude(seg => seg.GroupSegmentTemplate)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .AsSplitQuery()
            .Where(a => a.OrganizationId == organizationId && a.CompanyId == companyId &&
                a.Segments.Any(s => s.PlannedStart >= dayStart && s.PlannedStart < dayEnd))
            .OrderBy(a => a.Segments.Min(s => s.PlannedStart)).ThenBy(a => a.Id)
            .ToListAsync();
    }

    /// <summary>Termini na kojima klijent ima BILO KOJI Booking redak (bilo kojeg statusa — povijest uključuje i
    /// Cancelled/NoShow, isto kao prije uvođenja Bookinga).</summary>
    public async Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<Appointment> query = IncludeGraph(context.Appointments)
            .Include(a => a.Group)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .Where(a => a.OrganizationId == organizationId && a.Bookings.Any(b => b.ClientId == clientId));

        int totalCount = await query.CountAsync();

        List<Appointment> items = await query
            .OrderByDescending(a => a.Segments.Min(s => s.PlannedStart))
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
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(alloc => alloc.Payment)
            .Where(a => a.OrganizationId == organizationId &&
                // Phase M1A: "odrađeni termin zaposlenika" = njegov SEGMENT ima barem jedno Completed sudjelovanje
                // (izvršenje je po sudjelovanju; termin više nema Completed status).
                a.Segments.Any(s => s.Employees.Any(e => e.EmployeeId == employeeId) &&
                    s.Participations.Any(p => p.Status == ParticipationStatus.Completed)));

        int totalCount = await query.CountAsync();

        List<Appointment> items = await query
            .OrderByDescending(a => a.Segments.Min(s => s.PlannedStart))
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    /// <summary>Determinističan poredak (StartsAt pa Id) — GroupService.AddMember/RemoveMember zaključavaju
    /// Appointment redak po occurrence-u dok iteriraju ovaj rezultat (kapacitet/promocija), pa dva konkurentna
    /// poziva preko istog skupa budućih termina MORAJU zaključavati istim redoslijedom da se izbjegne deadlock
    /// (vidi GroupCapacityGuard/WaitlistService.PromoteEligibleWaiters).</summary>
    public Task<List<Appointment>> GetFutureScheduledForGroup(IUnitOfWork uow, Guid organizationId, Guid groupId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return IncludeFrame(uow.Context.Appointments)
            .Include(a => a.Bookings)
            .Where(a =>
                a.OrganizationId == organizationId &&
                a.GroupId == groupId &&
                a.Status == AppointmentStatus.Scheduled &&
                a.Segments.Any(s => s.PlannedStart >= now))
            .OrderBy(a => a.Segments.Min(s => s.PlannedStart)).ThenBy(a => a.Id)
            .ToListAsync();
    }

    public async Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return await context.AppointmentSegments.AnyAsync(s =>
            s.OrganizationId == organizationId &&
            s.Employees.Any(e => e.EmployeeId == employeeId) &&
            s.Appointment.Status == AppointmentStatus.Scheduled &&
            s.PlannedStart >= now);
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
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // Phase M0/M1A: segmentno — Confirmed sudjelovanje na budućem SEGMENTU (Confirmed sudjelovanje po izvođenju znači da je termin Scheduled).
        return await context.BookingSegmentParticipations.AnyAsync(p =>
            p.OrganizationId == organizationId &&
            p.Booking.ClientId == clientId &&
            p.Status == ParticipationStatus.Confirmed &&
            p.Segment.PlannedStart >= now);
    }

    /// <summary>FOR UPDATE preko FromSqlInterpolated (parametrizirano, sigurno od SQL injection) — Postgres Read
    /// Committed onda blokira konkurentni poziv nad ISTIM terminom dok se ova transakcija ne commita/rollbacka,
    /// nakon čega konkurentni poziv čita već-commitano stanje (svježi SELECT po statementu). Include(a => a.Group)
    /// se komponira nad FromSql rezultatom (podržano u EF Core/Npgsql).</summary>
    public async Task<Appointment> GetForUpdateWithGroup(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .Include(a => a.Group)
            .AsSplitQuery()
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetForSegmentMutation(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .Include(a => a.Segments).ThenInclude(s => s.Resources)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.PackageConsumptions)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations).ThenInclude(p => p.CheckoutItems)
            .AsSplitQuery()
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetLockedSegmentState(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .Include(a => a.Segments).ThenInclude(s => s.Resources)
            .Include(a => a.Bookings).ThenInclude(b => b.Participations)
            .AsSplitQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync();
    }

    public async Task<Appointment> GetForUpdateWithBookings(IUnitOfWork uow, Guid organizationId, Guid appointmentId)
    {
        return await uow.Context.Appointments
            .FromSqlInterpolated($"SELECT * FROM dunelight.appointments WHERE organization_id = {organizationId} AND id = {appointmentId} FOR UPDATE")
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .Include(a => a.Bookings)
            .SingleOrDefaultAsync();
    }

    public async Task<int> CountConfirmedOnSegment(Guid organizationId, Guid appointmentSegmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations.CountAsync(p =>
            p.OrganizationId == organizationId && p.AppointmentSegmentId == appointmentSegmentId && p.Status == ParticipationStatus.Confirmed);
    }

    public Task<int> CountConfirmedOnSegment(IUnitOfWork uow, Guid organizationId, Guid appointmentSegmentId)
    {
        return uow.Context.BookingSegmentParticipations.CountAsync(p =>
            p.OrganizationId == organizationId && p.AppointmentSegmentId == appointmentSegmentId && p.Status == ParticipationStatus.Confirmed);
    }

    public async Task<Dictionary<Guid, int>> GetNoShowCountsByClientIds(Guid organizationId, List<Guid> clientIds)
    {
        if (clientIds.Count == 0)
            return new Dictionary<Guid, int>();

        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations
            .Where(p =>
                clientIds.Contains(p.Booking.ClientId) &&
                p.OrganizationId == organizationId &&
                p.Status == ParticipationStatus.NoShow)
            .GroupBy(p => p.Booking.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ClientId, g => g.Count);
    }

    public async Task AddRange(IUnitOfWork uow, List<Appointment> appointments)
    {
        if (appointments.Count == 0)
            return;

        uow.Context.Appointments.AddRange(appointments);
        await uow.Context.SaveChangesAsync();
    }

    /// <summary>Jedan upit nad Booking — individualni i grupni termini dijele istu tablicu, pa više nema potrebe
    /// za odvojenim AppointmentClient/AppointmentAttendance granama (vidi Booking.cs).</summary>
    public async Task<ClientAppointmentStatsDto> GetStatsForClient(Guid organizationId, Guid clientId, List<Guid> activeGroupIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Phase M0: povijest klijenta broji IZVRŠNE jedinice (sudjelovanja) i čita vrijeme njihovog segmenta.
        IQueryable<BookingSegmentParticipation> participations = context.BookingSegmentParticipations
            .Where(p => p.Booking.ClientId == clientId && p.OrganizationId == organizationId);

        int completed = await participations.CountAsync(p => p.Status == ParticipationStatus.Completed);
        int noShow = await participations.CountAsync(p => p.Status == ParticipationStatus.NoShow);
        int cancelled = await participations.CountAsync(p => p.Status == ParticipationStatus.Cancelled);

        DateTimeOffset? lastVisit = await participations
            .Where(p => p.Status == ParticipationStatus.Completed)
            .Select(p => (DateTimeOffset?)p.Segment.PlannedStart)
            .MaxAsync();

        DateTimeOffset? nextVisit = await participations
            .Where(p => p.Status == ParticipationStatus.Confirmed && p.Segment.PlannedStart > now)
            .Select(p => (DateTimeOffset?)p.Segment.PlannedStart)
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
