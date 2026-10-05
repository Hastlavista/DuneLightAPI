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
/// sad rijedak slučaj jer GroupService.GenerateAppointments unaprijed stvara Confirmed sudjelovanje za svakog
/// aktivnog člana NA SEGMENTIMA ODABRANIH PREDLOŽAKA (Phase M1F.1: "očekivani" su po segmentu i po odabiru predložaka).
/// </summary>
public class GroupAttendanceService : IGroupAttendanceService
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IBookingService _bookingService;
    private readonly IGroupHandler _groupHandler;

    public GroupAttendanceService(IAppointmentHandler appointmentHandler, IBookingService bookingService, IGroupHandler groupHandler)
    {
        _appointmentHandler = appointmentHandler;
        _bookingService = bookingService;
        _groupHandler = groupHandler;
    }

    public async Task<GroupAttendanceListDto> GetAttendance(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(appointment, await LoadGroup(organizationId, appointment));
    }

    /// <summary>Grupa s aktivnim članovima, njihovim odabirom predložaka i predlošcima (usluga) — izvor "očekivanih".</summary>
    private Task<Group> LoadGroup(Guid organizationId, Appointment appointment) =>
        appointment.GroupId.HasValue ? _groupHandler.GetById(organizationId, appointment.GroupId.Value) : Task.FromResult<Group>(null);

    public async Task<GroupAttendanceListDto> SetAttendance(
        Guid organizationId, Guid userId, bool hasFullScope, Guid appointmentId, SetGroupAttendanceRequest request)
    {
        await LoadGroupAppointmentOrThrow(organizationId, appointmentId);

        await _bookingService.SetStatusOnSegment(organizationId, userId, hasFullScope, appointmentId, request.ClientId, new BookingSetStatusRequest
        {
            Status = request.Attended ? BookingStatus.Completed : BookingStatus.NoShow,
            ClientPackageId = request.ClientPackageId,
            PaymentMethod = request.PaymentMethod,
            Amount = request.Amount,
            IsPaid = request.IsPaid,
            Note = request.Note,
            SegmentId = request.SegmentId
        });

        Appointment refreshed = await LoadGroupAppointmentOrThrow(organizationId, appointmentId);
        return BuildListDto(refreshed, await LoadGroup(organizationId, refreshed));
    }

    private async Task<Appointment> LoadGroupAppointmentOrThrow(Guid organizationId, Guid appointmentId)
    {
        Appointment appointment = await _appointmentHandler.GetWithBookingsForMutation(organizationId, appointmentId);
        if (appointment == null || appointment.Form != AppointmentForm.Group)
            throw new NotFoundAppException("Appointment", appointmentId);

        return appointment;
    }

    /// <summary>
    /// Phase M1F.1 — prisutnost po SEGMENTU je izvedena iz odabira predložaka i konkretnih sudjelovanja:
    /// <list type="bullet">
    /// <item>Recorded segmenta = sudjelovanja na tom segmentu (istina occurrencea; gosti uključeni, IsMember=false).</item>
    /// <item>Expected segmenta = aktivni članovi koji su odabrali predložak TOG segmenta, a nemaju sudjelovanje na njemu — samo
    /// za segment koji još nije počeo; član koji je odabrao samo drugi predložak se tu nikad ne pojavljuje. Počet/prošli
    /// segment: sudjelovanja su jedina istina (današnje članstvo ne prepisuje povijest).</item>
    /// </list>
    /// </summary>
    private static GroupAttendanceListDto BuildListDto(Appointment appointment, Group group)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<GroupMember> activeMembers = group?.Members.Where(m => m.IsActive).ToList() ?? new List<GroupMember>();

        bool SelectsTemplate(Guid clientId, Guid? templateId) => templateId.HasValue && activeMembers.Any(m =>
            m.ClientId == clientId && m.SegmentTemplates.Any(x => x.GroupSegmentTemplateId == templateId.Value));

        List<GroupSegmentAttendanceDto> segments = appointment.Segments
            .OrderBy(s => s.PlannedStart).ThenBy(s => s.Id)
            .Select(segment =>
            {
                List<(Booking Booking, BookingSegmentParticipation Participation)> onSegment = appointment.Bookings
                    .SelectMany(b => b.Participations.Where(p => p.AppointmentSegmentId == segment.Id).Select(p => (b, p)))
                    .ToList();
                GroupSegmentTemplate template = group?.SegmentTemplates.SingleOrDefault(t => t.Id == segment.GroupSegmentTemplateId);

                List<GroupAttendanceEntryDto> expected = segment.PlannedStart > now && !appointment.IsExplicitlyCancelled
                    ? activeMembers
                        .Where(m => SelectsTemplate(m.ClientId, segment.GroupSegmentTemplateId) && onSegment.All(x => x.Booking.ClientId != m.ClientId))
                        .Select(m => new GroupAttendanceEntryDto
                        {
                            ClientId = m.ClientId,
                            ClientName = m.Client != null ? $"{m.Client.FirstName} {m.Client.LastName}" : null,
                            Attended = null,
                            IsMember = true
                        })
                        .ToList()
                    : new List<GroupAttendanceEntryDto>();

                List<GroupAttendanceEntryDto> recorded = onSegment.Select(x =>
                {
                    ParticipationSettlement settlement = ParticipationSettlement.Of(x.Participation);
                    PackageCoverageView coverage = PackageConsumptions.CoverageOf(x.Participation, AppointmentForm.Group);
                    return new GroupAttendanceEntryDto
                    {
                        ClientId = x.Booking.ClientId,
                        ClientName = x.Booking.Client != null ? $"{x.Booking.Client.FirstName} {x.Booking.Client.LastName}" : null,
                        Attended = ToAttended(BookingSummary.StatusOf(new[] { x.Participation.Status })),
                        CoverageType = coverage.CoverageType,
                        ClientPackageId = coverage.ClientPackageId,
                        PackageCoverageApplied = coverage.PackageCoverageApplied,
                        PackageCoverageReturned = coverage.PackageCoverageReturned,
                        Amount = x.Participation.Amount,
                        SuggestedAmount = x.Participation.SuggestedAmount,
                        PaidAmount = settlement.SettledAmount,
                        OutstandingAmount = settlement.OutstandingAmount,
                        IsPaid = settlement.FullySettled,
                        Note = x.Booking.Note,
                        IsMember = SelectsTemplate(x.Booking.ClientId, segment.GroupSegmentTemplateId),
                        ParticipationId = x.Participation.Id
                    };
                }).ToList();

                return new GroupSegmentAttendanceDto
                {
                    SegmentId = segment.Id.GetValueOrDefault(),
                    SegmentTemplateId = segment.GroupSegmentTemplateId,
                    ServiceId = segment.ServiceId,
                    ServiceName = template?.Service?.Name ?? segment.Service?.Name,
                    PlannedStart = segment.PlannedStart,
                    PlannedEnd = segment.PlannedEnd,
                    Expected = expected,
                    Recorded = recorded
                };
            })
            .ToList();

        return new GroupAttendanceListDto { Segments = segments };
    }

    /// <summary>Confirmed (još nije čekiran) mapira se na null (isto kao staro Attended=null prije prvog
    /// čekiranja) — Completed/NoShow mapiraju se na true/false, Cancelled (booking-razina otkazivanje
    /// izvan ovog ugovora, npr. buduća per-booking funkcionalnost) tretira se kao "nije prisutan".</summary>
    private static bool? ToAttended(BookingStatusSummary status) => status switch
    {
        BookingStatusSummary.Completed => true,
        BookingStatusSummary.NoShow => false,
        BookingStatusSummary.Cancelled => false,
        _ => null
    };
}
