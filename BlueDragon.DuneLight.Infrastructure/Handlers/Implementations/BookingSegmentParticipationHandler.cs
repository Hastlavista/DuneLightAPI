using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class BookingSegmentParticipationHandler : IBookingSegmentParticipationHandler
{
    private const string UniqueBookingSegmentIndex = "ux_booking_segment_participations_booking_segment";

    private readonly DatabaseSettings _databaseSettings;

    public BookingSegmentParticipationHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(BookingSegmentParticipation participation)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        Guid organizationId = participation.OrganizationId;

        // Booking.AppointmentId i AppointmentSegment.AppointmentId se nikad ne mijenjaju nakon nastanka, pa provjera
        // "isti termin" ovdje nije podložna utrci; jedinstvenost para dodatno čuva indeks u bazi.
        Guid? bookingAppointmentId = await context.Bookings
            .Where(b => b.OrganizationId == organizationId && b.Id == participation.BookingId)
            .Select(b => (Guid?)b.AppointmentId)
            .SingleOrDefaultAsync();
        if (bookingAppointmentId == null)
            throw new NotFoundAppException("Booking", participation.BookingId);

        Guid? segmentAppointmentId = await context.AppointmentSegments
            .Where(s => s.OrganizationId == organizationId && s.Id == participation.AppointmentSegmentId)
            .Select(s => (Guid?)s.AppointmentId)
            .SingleOrDefaultAsync();
        if (segmentAppointmentId == null)
            throw new NotFoundAppException("AppointmentSegment", participation.AppointmentSegmentId);

        if (bookingAppointmentId != segmentAppointmentId)
            throw new BusinessRuleException(ErrorCodes.ParticipationAppointmentMismatch,
                "Booking i segment ne pripadaju istom terminu — sudjelovanje nije moguće.");

        bool exists = await context.BookingSegmentParticipations.AnyAsync(p =>
            p.BookingId == participation.BookingId && p.AppointmentSegmentId == participation.AppointmentSegmentId);
        if (exists)
            throw DuplicateParticipation();

        participation.StatusVersion = 0;
        context.BookingSegmentParticipations.Add(participation);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueBookingSegmentIndex })
        {
            throw DuplicateParticipation();
        }
    }

    public async Task<BookingSegmentParticipation> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task<Guid?> GetAppointmentIdOf(Guid organizationId, Guid participationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Id == participationId)
            .Select(p => (Guid?)p.Booking.AppointmentId)
            .SingleOrDefaultAsync();
    }

    /// <summary>Phase M0: JEDINI redoslijed zaključavanja sudjelovanja — distinct, uzlazno po Id-u. Dvije transakcije
    /// koje zaključavaju preklapajuće skupove uvijek čekaju istim redom, pa se ne mogu zaključati u krug.</summary>
    public static IReadOnlyList<Guid> LockOrder(IEnumerable<Guid> participationIds) =>
        participationIds.Distinct().OrderBy(x => x).ToList();

    public async Task LockForUpdate(IUnitOfWork uow, Guid organizationId, IEnumerable<Guid> participationIds, CancellationToken cancellationToken = default)
    {
        foreach (Guid id in LockOrder(participationIds))
            await uow.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM dunelight.booking_segment_participations WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE",
                cancellationToken);
    }

    public async Task<Booking> GetBookingWithLockedParticipation(
        IUnitOfWork uow, Guid organizationId, Guid participationId, CancellationToken cancellationToken = default)
    {
        Guid? bookingId = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.Id == participationId)
            .Select(p => (Guid?)p.BookingId)
            .SingleOrDefaultAsync(cancellationToken);
        if (bookingId == null)
            return null;

        await LockForUpdate(uow, organizationId, new[] { participationId }, cancellationToken);
        return await LoadBookingGraph(uow, organizationId, bookingId.Value, cancellationToken);
    }

    public async Task<Booking> GetBookingWithLockedParticipations(
        IUnitOfWork uow, Guid organizationId, Guid bookingId, CancellationToken cancellationToken = default)
    {
        List<Guid> ids = await uow.Context.BookingSegmentParticipations
            .Where(p => p.OrganizationId == organizationId && p.BookingId == bookingId)
            .Select(p => p.Id.Value)
            .ToListAsync(cancellationToken);

        await LockForUpdate(uow, organizationId, ids, cancellationToken);
        return await LoadBookingGraph(uow, organizationId, bookingId, cancellationToken);
    }

    /// <summary>Svježe čitanje NAKON locka (Read Committed: novi statement vidi commitano stanje). Učitava samo Booking i
    /// (AutoInclude) njegova sudjelovanja s potrošnjama paketa — isti praćeni graf kao bivši Booking FOR UPDATE, tako da
    /// pozivateljev UpdateBooking ne dira stavke namirenja (one se čitaju zasebno, npr. GetItemsForParticipation).</summary>
    private static Task<Booking> LoadBookingGraph(IUnitOfWork uow, Guid organizationId, Guid bookingId, CancellationToken cancellationToken)
    {
        return uow.Context.Bookings
            .SingleOrDefaultAsync(b => b.OrganizationId == organizationId && b.Id == bookingId, cancellationToken);
    }

    public async Task<List<BookingSegmentParticipation>> GetForBooking(Guid organizationId, Guid bookingId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations.AsNoTracking()
            .Include(p => p.Segment)
            .Where(p => p.OrganizationId == organizationId && p.BookingId == bookingId)
            .OrderBy(p => p.Segment.PlannedStart)
            .ToListAsync();
    }

    public async Task<List<BookingSegmentParticipation>> GetForSegment(Guid organizationId, Guid appointmentSegmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.AppointmentSegmentId == appointmentSegmentId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> ExistsForAppointment(Guid organizationId, Guid appointmentId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.BookingSegmentParticipations
            .AnyAsync(p => p.OrganizationId == organizationId && p.Booking.AppointmentId == appointmentId);
    }

    private static BusinessRuleException DuplicateParticipation() =>
        new(ErrorCodes.DuplicateParticipation, "Booking već sudjeluje u ovom segmentu.");
}
