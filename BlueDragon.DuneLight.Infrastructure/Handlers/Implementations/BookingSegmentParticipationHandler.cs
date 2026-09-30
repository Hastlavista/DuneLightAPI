using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
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
