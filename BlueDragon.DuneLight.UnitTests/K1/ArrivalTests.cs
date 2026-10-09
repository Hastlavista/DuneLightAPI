#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-2 (P-20) — dolazak je metapodatak (ne status, bez financijskog učinka): Confirmed/Completed, i prije početka; izostanak i
/// otkaz ga brišu uz trajni trag u povijesti. K1-9 — dolazak i odrada sesije s neplaćenim dugom upozoravaju recepciju.
/// </summary>
public class ArrivalTests
{
    private static async Task<(AppointmentDto Appointment, Guid ParticipationId)> Booked(SchedulingWorld w, DateTimeOffset start)
    {
        AppointmentDto created = await w.CreateAppointment(start);
        return (created, await w.ParticipationIdOnOnlySegment(created.Id, w.Client.Id.Value));
    }

    private static BookingParticipationDto Only(BookingDto booking) => Assert.Single(booking.Participations);

    private static async Task<List<AppointmentAuditLog>> ArrivalAudit(SchedulingWorld w, Guid appointmentId) =>
        (await w.LoadAuditLog(appointmentId)).Where(a => a.ChangeType == "Arrival").OrderBy(a => a.ChangedAt).ToList();

    [Fact]
    public async Task Mark_BeforeStart_RecordsWhoAndWhen_WithoutTouchingStatusOrMoney()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Mark_BeforeStart_RecordsWhoAndWhen_WithoutTouchingStatusOrMoney));
        (AppointmentDto created, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));

        BookingDto marked = await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);

        BookingParticipationDto p = Only(marked);
        Assert.Equal(BookingStatus.Confirmed, p.Status);
        Assert.NotNull(p.ArrivedAt);
        Assert.Equal(w.ActorUserId, p.ArrivedBy);
        Assert.Equal(0, p.StatusVersion);
        Assert.Empty(await w.LoadPayments(marked.Id));
        Assert.Single(await ArrivalAudit(w, created.Id));

        // Ponovno označavanje ne mijenja prvi zapis.
        BookingDto again = await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);
        Assert.Equal(p.ArrivedAt, Only(again).ArrivedAt);
        Assert.Single(await ArrivalAudit(w, created.Id));
    }

    [Fact]
    public async Task Clear_RemovesTheArrival_AndKeepsTheTrail()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Clear_RemovesTheArrival_AndKeepsTheTrail));
        (AppointmentDto created, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));
        await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);

        BookingDto cleared = await w.Bookings.ClearArrival(w.OrganizationId, w.ActorUserId, participationId);

        Assert.Null(Only(cleared).ArrivedAt);
        Assert.Null(Only(cleared).ArrivedBy);
        List<AppointmentAuditLog> trail = await ArrivalAudit(w, created.Id);
        Assert.Equal(2, trail.Count);
        Assert.Equal("Cleared:Manual", trail[1].NewValue);
        Assert.Contains(w.ActorUserId.ToString(), trail[1].OldValue);
    }

    [Fact]
    public async Task CancelledParticipation_CannotBeMarked()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CancelledParticipation_CannotBeMarked));
        (AppointmentDto created, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "late");

        await SchedulingAssert.BusinessRule(ErrorCodes.ParticipationArrivalNotAllowed,
            () => w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId));
    }

    [Fact]
    public async Task NoShowAfterArrival_ClearsTheArrival_WithATrail_AndCompletionKeepsIt()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NoShowAfterArrival_ClearsTheArrival_WithATrail_AndCompletionKeepsIt));
        (AppointmentDto created, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));
        await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);
        await w.MoveToPast(created.Id);

        // P-20: trener smije kasnije staviti "nije se pojavio" — dolazak se briše, povijest pamti tko ga je označio.
        BookingDto noShow = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.NoShow });
        Assert.Equal(BookingStatus.NoShow, Only(noShow).Status);
        Assert.Null(Only(noShow).ArrivedAt);
        AppointmentAuditLog cleared = (await ArrivalAudit(w, created.Id)).Last();
        Assert.Equal("Cleared:NoShow", cleared.NewValue);
        Assert.Contains(w.ActorUserId.ToString(), cleared.OldValue);

        // Korekcija natrag pa ponovni dolazak; odrada ("stigao" nije uvjet, ali kad postoji) ga zadržava.
        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Confirmed });
        await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);
        BookingDto completed = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, PaymentMethod = PaymentMethod.Cash });
        Assert.Equal(BookingStatus.Completed, Only(completed).Status);
        Assert.NotNull(Only(completed).ArrivedAt);
    }

    [Fact]
    public async Task Completion_WithoutArrival_IsAllowed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Completion_WithoutArrival_IsAllowed));
        (_, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));

        BookingDto completed = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, PaymentMethod = PaymentMethod.Cash });

        Assert.Equal(BookingStatus.Completed, Only(completed).Status);
        Assert.Null(Only(completed).ArrivedAt);
    }

    #region K1-9 — upozorenje "nije pokriveno"

    [Fact]
    public async Task Arrival_UnpaidWithoutPackage_WarnsNotCovered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Arrival_UnpaidWithoutPackage_WarnsNotCovered));
        (_, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));

        BookingDto marked = await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);

        WarningDto warning = Assert.Single(marked.Warnings);
        Assert.Equal(WarningCodes.ParticipationNotCovered, warning.Code);
        WarningParticipationCoverageDetails details = Assert.IsType<WarningParticipationCoverageDetails>(warning.Details);
        Assert.Equal(participationId, details.ParticipationId);
        Assert.Equal(SchedulingWorld.DefaultServicePrice, details.OutstandingAmount);
        Assert.Null(details.MembershipCoverageStatus);
    }

    [Fact]
    public async Task Arrival_WithAnEligibleUnselectedPackage_WarnsPackageAvailable()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Arrival_WithAnEligibleUnselectedPackage_WarnsPackageAvailable));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, SchedulingWorld.FutureDay.AddYears(1));
        (_, Guid participationId) = await Booked(w, SchedulingWorld.Future(10));

        BookingDto marked = await w.Bookings.MarkArrival(w.OrganizationId, w.ActorUserId, participationId);

        WarningDto warning = Assert.Single(marked.Warnings);
        Assert.Equal(WarningCodes.ParticipationPackageAvailable, warning.Code);
        WarningParticipationCoverageDetails details = Assert.IsType<WarningParticipationCoverageDetails>(warning.Details);
        Assert.Equal(package.Id, Assert.Single(details.EligiblePackages).ClientPackageId);
    }

    [Fact]
    public async Task Completion_PaidInCashOrByPackage_DoesNotWarn_UnpaidDoes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Completion_PaidInCashOrByPackage_DoesNotWarn_UnpaidDoes));
        (_, Guid paid) = await Booked(w, SchedulingWorld.Future(9));
        BookingDto cash = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, paid,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, PaymentMethod = PaymentMethod.Cash });
        Assert.Empty(cash.Warnings);

        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, SchedulingWorld.FutureDay.AddYears(1));
        (_, Guid byPackage) = await Booked(w, SchedulingWorld.Future(11));
        BookingDto covered = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, byPackage,
            new BookingSetStatusRequest { Status = BookingStatus.Completed, ClientPackageId = package.Id });
        Assert.Empty(covered.Warnings);

        (_, Guid unpaid) = await Booked(w, SchedulingWorld.Future(13));
        BookingDto open = await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, unpaid,
            new BookingSetStatusRequest { Status = BookingStatus.Completed });
        Assert.Equal(WarningCodes.ParticipationPackageAvailable, Assert.Single(open.Warnings).Code);
    }

    [Fact]
    public async Task GroupAttendance_UnpaidWithoutPackage_WarnsNotCovered_PaidDoesNot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupAttendance_UnpaidWithoutPackage_WarnsNotCovered_PaidDoesNot));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 5);
        Client paying = await w.AddClient("Paying", "Member");
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, paying);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.MoveToPast(occurrence.Id.Value);
        Guid segmentId = occurrence.Segments.Single().Id.Value;

        GroupAttendanceListDto unpaid = await w.GroupAttendance.SetAttendance(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new SetGroupAttendanceRequest { ClientId = w.Client.Id.Value, Attended = true, SegmentId = segmentId });
        WarningDto warning = Assert.Single(unpaid.Warnings);
        Assert.Equal(WarningCodes.ParticipationNotCovered, warning.Code);
        Assert.Equal(w.Client.Id, Assert.IsType<WarningParticipationCoverageDetails>(warning.Details).ClientId);

        GroupAttendanceListDto paid = await w.GroupAttendance.SetAttendance(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value,
            new SetGroupAttendanceRequest { ClientId = paying.Id.Value, Attended = true, SegmentId = segmentId, PaymentMethod = PaymentMethod.Cash });
        Assert.Empty(paid.Warnings);
    }

    [Fact]
    public async Task CompleteNow_Unpaid_WarnsNotCovered()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_Unpaid_WarnsNotCovered));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        Assert.Contains(dto.Warnings, x => x.Code == WarningCodes.ParticipationNotCovered);
    }

    #endregion
}
