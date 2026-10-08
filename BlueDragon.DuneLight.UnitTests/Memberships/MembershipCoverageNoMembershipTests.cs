#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.UnitTests.Scheduling;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Memberships;

/// <summary>
/// P2 phase 2D — regression guard written BEFORE the coverage hooks (emphasis 1): a client WITHOUT a membership behaves exactly
/// as before P2 — booking, late cancellation and no-show under P1 (fee as the only due), completion with payment and group
/// occurrences — even in an organization where another client holds a membership covering the same services. No coverage
/// projection and no usage ledger row is ever written for such a client. The P1 characterization suites stay the full
/// comparison; this test pins the "nothing membership-related" guarantee itself.
/// </summary>
public class MembershipCoverageNoMembershipTests
{
    private static DateTimeOffset NearFuture(int days, int hour) => new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(days).AddHours(hour);

    private static async Task<BookingParticipationDto> OnlyParticipation(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single().Participations.Single();

    [Fact]
    public async Task ClientWithoutMembership_HasExactlyThePreP2Behaviour_AndNothingMembershipRelatedIsWritten()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientWithoutMembership_HasExactlyThePreP2Behaviour_AndNothingMembershipRelatedIsWritten));
        ServiceEntity groupService = await w.AddGroupService(price: 15m);

        // Another client holds a membership covering both services, so every coverage hook is live in this organization.
        MembershipPlanDto plan = await w.Resolve<IMembershipPlanService>().Create(w.OrganizationId, w.ActorUserId, new MembershipPlanCreateRequest
        {
            Name = "Gold", Price = 50m, BillingInterval = MembershipBillingInterval.Monthly, RenewalAnchor = MembershipRenewalAnchor.PurchaseDate,
            CompanyScope = MembershipCompanyScope.AllCompanies,
            Services = new List<MembershipPlanCoveredServiceRequest> { new() { ServiceId = w.Service.Id }, new() { ServiceId = groupService.Id } },
            // 2E: a price benefit is live in the organization too.
            PriceBenefits = new List<MembershipPriceBenefitDto> { new() { Scope = MembershipPriceBenefitScope.AllServices, Type = MembershipPriceBenefitType.PercentOff, Value = 50m } },
            Pause = new MembershipPauseRulesDto { Allowed = false }
        });
        Client member = await w.AddClient("Member", "Client");
        await w.Resolve<IClientMembershipService>().Sell(w.OrganizationId, w.ActorUserId, member.Id.Value, new ClientMembershipSellRequest
        {
            MembershipPlanId = plan.Id, SoldCompanyId = w.Company.Id.Value
        });

        // P1 late cancellation (40 % fee) of a near-future booking: the fee is the only due.
        DateTimeOffset start = NearFuture(2, 10);
        await w.PublishDefaultPolicyVersion((int)(start - DateTimeOffset.UtcNow).TotalMinutes + 60, CancellationFeeType.Percentage, 40m,
            noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 15m);
        AppointmentDto lateCancelled = await w.CreateAppointment(w.CreateRequest(start, overrideAvailability: true));
        await w.SetBookingStatus(lateCancelled.Id, w.Client, BookingStatus.Cancelled, "sick");
        BookingParticipationDto late = await OnlyParticipation(w, lateCancelled.Id);
        Assert.Equal((true, 50m, 20m, 20m), (late.IsLateCancellation.Value, late.Amount, late.MonetaryDue, late.OutstandingAmount));

        // P1 no-show: the fixed fee is the due.
        AppointmentDto noShow = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(noShow.Id, w.Client, BookingStatus.NoShow, "absent");
        BookingParticipationDto absent = await OnlyParticipation(w, noShow.Id);
        Assert.Equal((15m, 15m), (absent.MonetaryDue, absent.OutstandingAmount));

        // Confirmed booking: the price is due; completion with a payment settles it.
        AppointmentDto confirmed = await w.CreateAppointment(w.CreateRequest(NearFuture(3, 12), overrideAvailability: true));
        Assert.Equal((50m, 50m), ((await OnlyParticipation(w, confirmed.Id)).MonetaryDue, (await OnlyParticipation(w, confirmed.Id)).OutstandingAmount));
        AppointmentDto completed = await w.CreateAppointment(SchedulingWorld.Past(12));
        await w.SetBookingStatus(completed.Id, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash);
        BookingParticipationDto paid = await OnlyParticipation(w, completed.Id);
        Assert.Equal((50m, 0m, true), (paid.PaidAmount, paid.OutstandingAmount, paid.IsPaid));

        // Group occurrence generation with the non-member as a group member.
        GroupDto group = await w.CreateGroup(groupService, capacity: 5);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        BookingParticipationDto groupParticipation = (await w.Appointments.GetById(w.OrganizationId, occurrence.Id.Value))
            .Bookings.Single(b => b.ClientId == w.Client.Id).Participations.Single();
        Assert.Equal(15m, groupParticipation.MonetaryDue);
        Assert.All(new[] { late, absent, paid, groupParticipation }, p => Assert.Equal((null, null), (p.PriceAdjustment, p.MembershipCoverage)));

        await using DatabaseContext db = w.NewDb();
        List<Guid> participationIds = await db.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.OrganizationId == w.OrganizationId && p.Booking.ClientId == w.Client.Id)
            .Select(p => p.Id.Value).ToListAsync();
        Assert.Equal(5, participationIds.Count);
        Assert.Empty(await db.Set<ParticipationMembershipCoverage>().AsNoTracking().Where(c => participationIds.Contains(c.ParticipationId)).ToListAsync());
        Assert.Empty(await db.Set<MembershipUsage>().AsNoTracking().Where(u => participationIds.Contains(u.ParticipationId)).ToListAsync());
        Assert.All(await db.ParticipationPolicyConsequences.AsNoTracking().Where(c => participationIds.Contains(c.BookingSegmentParticipationId)).ToListAsync(),
            c => Assert.Equal((null, false), ((CancellationMembershipAction?)c.MembershipAction, c.MembershipCreditForfeited)));
    }
}
