using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Groups;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Tanki adapter preko IBookingService koji čuva POSTOJEĆI /api/groups/appointments/{id}/attendance ugovor
/// (GroupAttendanceListDto/SetGroupAttendanceRequest, Attended bool umjesto BookingStatus) nakon uvođenja
/// Bookinga — frontend za grupnu prisutnost namjerno ostaje netaknut (vidi Booking.cs domensku napomenu),
/// jedino je AppointmentAttendance ispod zamijenjen Bookingom. "Expected" (aktivni članovi bez retka) je
/// sad rijedak slučaj jer GroupService.GenerateAppointments unaprijed stvara Confirmed Booking za svakog
/// aktivnog člana — može se pojaviti samo za članstvo dodano nakon generiranja prije nego što
/// AddMember-sinkronizacija stigne (ili na starim, prije-migracijskim podacima).
/// </summary>
public class GroupAttendanceService : IGroupAttendanceService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IBookingService _bookingService;

    public GroupAttendanceService(IAppointmentHandler appointmentHandler, IBookingService bookingService)
    {
        _appointmentHandler = appointmentHandler;
        _bookingService = bookingService;
    }

    public async Task<GroupAttendanceListDto> GetAttendance(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(appointment);
    }

    public async Task<GroupAttendanceListDto> SetAttendance(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, SetGroupAttendanceRequest request)
    {
        await LoadGroupAppointmentOrThrow(organizationId, appointmentId);

        await _bookingService.SetStatus(organizationId, userId, hasFullScope, appointmentId, request.ClientId, new BookingSetStatusRequest
        {
            Status = request.Attended ? BookingStatus.Completed : BookingStatus.NoShow,
            ClientPackageId = request.ClientPackageId,
            PaymentMethod = request.PaymentMethod,
            Amount = request.Amount,
            IsPaid = request.IsPaid,
            Note = request.Note
        });

        Appointment refreshed = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(refreshed);
    }

    private async Task<Appointment> LoadGroupAppointmentOrThrow(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null || appointment.Form != AppointmentForm.Group)
            throw new NotFoundAppException("Appointment", appointmentId);

        return appointment;
    }

    private static GroupAttendanceListDto BuildListDto(Appointment appointment)
    {
        List<Guid> recordedClientIds = appointment.Bookings.Select(b => b.ClientId).ToList();
        List<GroupMember> activeMembers = appointment.Group?.Members.Where(m => m.IsActive).ToList() ?? new List<GroupMember>();

        List<GroupAttendanceEntryDto> expected = activeMembers
            .Where(m => !recordedClientIds.Contains(m.ClientId))
            .Select(m => new GroupAttendanceEntryDto
            {
                ClientId = m.ClientId,
                ClientName = m.Client != null ? $"{m.Client.FirstName} {m.Client.LastName}" : null,
                Attended = null,
                IsMember = true
            })
            .ToList();

        List<GroupAttendanceEntryDto> recorded = appointment.Bookings
            .Select(b =>
            {
                decimal outstandingAmount = BookingFinancialsCalculator.CalculateOutstanding(b);
                return new GroupAttendanceEntryDto
                {
                    ClientId = b.ClientId,
                    ClientName = b.Client != null ? $"{b.Client.FirstName} {b.Client.LastName}" : null,
                    Attended = ToAttended(b.Status),
                    CoverageType = b.CoverageType,
                    ClientPackageId = b.ClientPackageId,
                    PackageCoverageApplied = b.PackageCoverageApplied,
                    PackageCoverageReturned = b.PackageCoverageReturned,
                    Amount = b.Amount,
                    SuggestedAmount = b.SuggestedAmount,
                    PaidAmount = BookingFinancialsCalculator.CalculatePaidAmount(b),
                    OutstandingAmount = outstandingAmount,
                    IsPaid = outstandingAmount <= 0m,
                    Note = b.Note,
                    IsMember = activeMembers.Any(m => m.ClientId == b.ClientId)
                };
            })
            .ToList();

        return new GroupAttendanceListDto { Expected = expected, Recorded = recorded };
    }

    /// <summary>Confirmed (još nije čekiran) mapira se na null (isto kao staro Attended=null prije prvog
    /// čekiranja) — Completed/NoShow mapiraju se na true/false, Cancelled (booking-razina otkazivanje
    /// izvan ovog ugovora, npr. buduća per-booking funkcionalnost) tretira se kao "nije prisutan".</summary>
    private static bool? ToAttended(BookingStatus status) => status switch
    {
        BookingStatus.Completed => true,
        BookingStatus.NoShow => false,
        BookingStatus.Cancelled => false,
        _ => null
    };
}
