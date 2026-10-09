#nullable disable
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.K1;

/// <summary>
/// K1-5 (14.1) — "vrati cijeli termin": samo sudjelovanja otkazana otkazom termina se vraćaju (klijentov raniji otkaz ostaje),
/// sve ili ništa uz provjeru preklapanja, povijest termina, istekla lista čekanja se ne vraća (popis u upozorenju).
/// </summary>
public class AppointmentRestoreTests
{
    [Fact]
    public async Task Restore_BringsBackOnlyWhatTheAppointmentCancelCancelled()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_BringsBackOnlyWhatTheAppointmentCancelCancelled));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second);
        await w.SetBookingStatus(created.Id, second, BookingStatus.Cancelled, "client cancelled earlier");
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        AppointmentDto restored = await w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id,
            new AppointmentRestoreRequest { Reason = "pogrešan klik" });

        Assert.Equal(AppointmentStatus.Scheduled, restored.Status);
        Assert.Null(restored.CancelledAt);
        Assert.Equal(BookingStatusSummary.Confirmed, restored.Bookings.Single(b => b.ClientId == w.Client.Id).Status);
        Assert.Equal(BookingStatusSummary.Cancelled, restored.Bookings.Single(b => b.ClientId == second.Id).Status);
        Assert.Contains(await w.LoadAuditLog(created.Id), a => a.ChangeType == "AppointmentRestored" && a.NewValue == "pogrešan klik");
    }

    [Fact]
    public async Task Restore_ReevaluatesMembershipCoverage_OverTheHistoricalPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_ReevaluatesMembershipCoverage_OverTheHistoricalPrice));
        DateTimeOffset start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(2).AddHours(10);
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(start, overrideAvailability: true));
        Assert.Equal(SchedulingWorld.DefaultServicePrice, Assert.Single(Assert.Single(created.Bookings).Participations).OutstandingAmount);
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());

        // Klijent u međuvremenu kupi članarinu koja pokriva uslugu — vraćanje evaluira pokriće kao nova rezervacija.
        MembershipPlanDto plan = await w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = "Gold", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        await w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, w.Client.Id.Value,
            new ClientMembershipSellRequest { MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value });

        AppointmentDto restored = await w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id, null);

        BookingParticipationDto p = Assert.Single(Assert.Single(restored.Bookings).Participations);
        Assert.Equal(MembershipCoverageStatus.Covered, p.MembershipCoverage.Status);
        Assert.Equal((SchedulingWorld.DefaultServicePrice, 0m), (p.Amount, p.OutstandingAmount)); // povijesna cijena, dug 0 (2E/Q9)
    }

    [Fact]
    public async Task Restore_OfANotCancelledAppointment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_OfANotCancelledAppointment_IsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentNotCancelled,
            () => w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id, null));
    }

    [Fact]
    public async Task Restore_IsAllOrNothing_WhenTheSlotWasTakenMeanwhile()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_IsAllOrNothing_WhenTheSlotWasTakenMeanwhile));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, SchedulingWorld.BusinessCancel());
        await w.CreateAppointment(SchedulingWorld.Future(10)); // isti zaposlenik i klijent u istom terminu

        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => w.Appointments.Restore(w.OrganizationId, w.ActorUserId, created.Id, null));
        Assert.NotNull((await w.LoadAppointment(created.Id)).CancelledAt);
    }

    [Fact]
    public async Task Restore_OfAGroupOccurrence_ListsTheExpiredWaitlist()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Restore_OfAGroupOccurrence_ListsTheExpiredWaitlist));
        ServiceEntity svc = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(svc, capacity: 1);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Client waiter = await w.AddClient("Waiter", "Client");
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new WaitlistJoinRequest
        {
            ClientId = waiter.Id.Value, SegmentId = occurrence.Segments.Single().Id
        });
        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, SchedulingWorld.BusinessCancel());

        AppointmentDto restored = await w.Appointments.Restore(w.OrganizationId, w.ActorUserId, occurrence.Id.Value, null);

        Assert.Equal(AppointmentStatus.Scheduled, restored.Status);
        WarningDto warning = Assert.Single(restored.Warnings, x => x.Code == WarningCodes.AppointmentRestoredWaitlistNotRestored);
        Assert.Equal(waiter.Id, Assert.Single(Assert.IsType<WarningWaitlistEntriesDetails>(warning.Details).Entries).ClientId);
        Assert.Equal(WaitlistEntryStatus.Expired, Assert.Single(await w.LoadWaitlist(occurrence.Id.Value)).Status);
    }
}
