#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B2 — BookingSegmentParticipation is the ONLY store of a booking's price (Amount, SuggestedAmount,
/// IsAmountManuallyOverridden); Booking keeps identity, package and settlement. BaseAmount/BaseAmountSource are the
/// TRUTHFUL price-list resolution snapshot (ResolvePriceResponse.Price/Source) and are set only when a price was actually
/// resolved; AdjustmentAmount is never written (the current pricing model has no explicit adjustment). Pricing is not
/// lifecycle history (ParticipationHistory is unchanged).
/// </summary>
public class BookingParticipationPricingTests
{
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);
    private static readonly DateTimeOffset LongAgo = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<BookingSegmentParticipation> ParticipationOf(SchedulingWorld w, Guid appointmentId, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.BookingSegmentParticipations.AsNoTracking()
            .SingleAsync(p => p.Booking.AppointmentId == appointmentId && p.Booking.ClientId == client.Id);
    }

    private static void AssertPrice(BookingSegmentParticipation p, decimal amount, decimal suggested, bool overridden,
        decimal? baseAmount, PriceSource? source)
    {
        Assert.Equal((amount, suggested, overridden), (p.Amount, p.SuggestedAmount, p.IsAmountManuallyOverridden));
        Assert.Equal(baseAmount, p.BaseAmount);
        Assert.Equal(source, p.BaseAmountSource);
        Assert.Null(p.AdjustmentAmount); // no adjustment semantics exist yet
    }

    #region Creation + truthful resolution snapshot

    [Fact]
    public async Task Create_AtTheDefaultPrice_SnapshotsTheResolutionOnTheParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_AtTheDefaultPrice_SnapshotsTheResolutionOnTheParticipation));

        AppointmentDto created = await w.CreateAppointment(Z(10));

        AssertPrice(await ParticipationOf(w, created.Id, w.Client), 50m, 50m, false, 50m, PriceSource.Default);
        BookingDto dto = Assert.Single(created.Bookings); // API contract unchanged, now fed from the participation
        Assert.Equal((50m, 50m, false), (dto.Amount, dto.SuggestedAmount, dto.IsAmountManuallyOverridden));
    }

    [Theory]
    [InlineData(true, PriceSource.CompanySpecific)]
    [InlineData(false, PriceSource.AllCompanies)]
    public async Task Create_FromAPriceListRow_RecordsWhichRowKindWasResolved(bool forCompany, PriceSource expected)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(Create_FromAPriceListRow_RecordsWhichRowKindWasResolved)}-{expected}");
        await w.AddPriceListItem(w.Service, 70m, validFrom: LongAgo, companyId: forCompany ? w.Company.Id : null);

        AppointmentDto created = await w.CreateAppointment(Z(10));

        AssertPrice(await ParticipationOf(w, created.Id, w.Client), 70m, 70m, false, 70m, expected);
    }

    [Fact]
    public async Task Create_WithAManualAmount_KeepsSuggestedAndOverrideOnTheParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_WithAManualAmount_KeepsSuggestedAndOverrideOnTheParticipation));

        AppointmentDto overridden = await w.CreateAppointment(w.CreateRequest(Z(10), amount: 30m));
        AppointmentDto sameAsSuggested = await w.CreateAppointment(w.CreateRequest(Z(12), amount: 50m));
        AppointmentDto free = await w.CreateAppointment(w.CreateRequest(Z(14), amount: 0m));

        AssertPrice(await ParticipationOf(w, overridden.Id, w.Client), 30m, 50m, true, 50m, PriceSource.Default);
        AssertPrice(await ParticipationOf(w, sameAsSuggested.Id, w.Client), 50m, 50m, false, 50m, PriceSource.Default);
        AssertPrice(await ParticipationOf(w, free.Id, w.Client), 0m, 50m, true, 50m, PriceSource.Default);
    }

    [Fact]
    public async Task CompleteNew_PricesTheParticipationFromTheSettlement()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_PricesTheParticipationFromTheSettlement));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), settlementAmount: 35m));

        AssertPrice(await ParticipationOf(w, dto.Id, w.Client), 35m, 50m, true, 50m, PriceSource.Default);
    }

    [Fact]
    public async Task EveryCreationPath_PricesItsParticipationFromAResolution_AndTheBookingHasNoPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(EveryCreationPath_PricesItsParticipationFromAResolution_AndTheBookingHasNoPrice));
        Client added = await w.AddClient("Added", "Client");

        // Individual: Create, recurring, CompleteNew, Update adding a client.
        AppointmentDto single = await w.CreateAppointment(Z(8));
        await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value, ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = Z(9), EndDate = Z(9).AddDays(14)
        });
        await w.CompleteNew(w.CompleteRequest(Z(13)));
        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, single.Id,
            w.UpdateRequest(single, r => r.ClientIds = new List<Guid> { w.Client.Id.Value, added.Id.Value }));

        // Group: generation, AddMember after generation, guest (AddBooking), waitlist promotion.
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 1);
        Client member = await w.AddClient("Member", "Client");
        await w.AddGroupMember(group, member);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Client waiter = await w.AddClient("Waiter", "Client");
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value });
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Cancelled, "cannot come"); // promotes the waiter
        GroupDto roomy = await w.CreateGroup(groupService, capacity: 5, slots: (DayOfWeek.Monday, TimeSpan.FromHours(16)));
        Appointment roomyOccurrence = await w.GenerateSingleOccurrence(roomy);
        Client guest = await w.AddClient("Guest", "Client");
        await w.AddGuest(roomyOccurrence, guest);
        Client lateMember = await w.AddClient("Late", "Member");
        await w.AddGroupMember(roomy, lateMember);

        await using DatabaseContext db = w.NewDb();
        List<BookingSegmentParticipation> all = await db.BookingSegmentParticipations.AsNoTracking()
            .Where(p => p.OrganizationId == w.OrganizationId).ToListAsync();
        Assert.Equal(await db.Bookings.CountAsync(b => b.OrganizationId == w.OrganizationId), all.Count);
        Assert.True(all.Count >= 10);
        Assert.All(all, p =>
        {
            Assert.Equal(p.SuggestedAmount, p.BaseAmount); // resolved price, no adjustment layer
            Assert.NotNull(p.BaseAmountSource);
            Assert.Null(p.AdjustmentAmount);
            Assert.True(p.SuggestedAmount > 0m);
        });
        Assert.Contains(all, p => p.SuggestedAmount == 15m); // group service price
        Assert.Contains(all, p => p.SuggestedAmount == 50m); // individual service price

        List<string> bookingColumns = await db.Database.SqlQueryRaw<string>(@"
            SELECT column_name AS ""Value"" FROM information_schema.columns
             WHERE table_schema = 'dunelight' AND table_name = 'bookings'
               AND column_name IN ('amount', 'suggested_amount', 'is_amount_manually_overridden')").ToListAsync();
        Assert.Empty(bookingColumns);
    }

    #endregion

    #region Repricing

    [Fact]
    public async Task Update_RepricesTheParticipations_AndPricingIsNotLifecycleHistory()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_RepricesTheParticipations_AndPricingIsNotLifecycleHistory));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);

        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r => r.Amount = 20m));

        BookingSegmentParticipation p = await ParticipationOf(w, created.Id, second);
        AssertPrice(p, 20m, 50m, true, 50m, PriceSource.Default);
        Assert.Equal(0, p.StatusVersion);
        Assert.True(ParticipationHistory.IsUntouched(p)); // a repriced participation is still deletable

        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, created.Id);
        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id,
            w.UpdateRequest(current, r => r.ClientIds = new List<Guid> { w.Client.Id.Value }));
        Assert.Equal(w.Client.Id, Assert.Single((await w.LoadAppointment(created.Id)).Bookings).ClientId);
    }

    [Fact]
    public async Task Update_AfterAPriceListChange_ReResolvesTheSnapshot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AfterAPriceListChange_ReResolvesTheSnapshot));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.AddPriceListItem(w.Service, 65m, validFrom: LongAgo, companyId: w.Company.Id);

        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created));

        AssertPrice(await ParticipationOf(w, created.Id, w.Client), 65m, 65m, false, 65m, PriceSource.CompanySpecific);
    }

    [Fact]
    public async Task CompleteExisting_RepricesTheParticipationFromTheSettlement()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_RepricesTheParticipationFromTheSettlement));
        AppointmentDto created = await w.CreateAppointment(Z(10));

        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), settlementAmount: 42m));

        AssertPrice(await ParticipationOf(w, created.Id, w.Client), 42m, 50m, true, 50m, PriceSource.Default);
    }

    [Fact]
    public async Task GroupUnCheckIn_ResetsThePriceToZero_WithoutAFabricatedResolutionSnapshot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupUnCheckIn_ResetsThePriceToZero_WithoutAFabricatedResolutionSnapshot));
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Completed, paymentMethod: PaymentMethod.Cash, amount: 12m);
        AssertPrice(await ParticipationOf(w, occurrence.Id.Value, w.Client), 12m, 15m, true, 15m, PriceSource.Default);

        await w.SetBookingStatus(occurrence.Id.Value, w.Client, BookingStatus.Confirmed);

        AssertPrice(await ParticipationOf(w, occurrence.Id.Value, w.Client), 0m, 0m, false, null, null);
    }

    #endregion

    #region Checkout / settlement read the participation price

    [Fact]
    public async Task Checkout_UsesTheParticipationPrice_ForItemAndOutstanding()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Checkout_UsesTheParticipationPrice_ForItemAndOutstanding));
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(Z(10), amount: 30m));
        Guid bookingId = created.Bookings.Single().Id;

        await w.PayBookingViaCheckout(bookingId, w.Client, 10m);

        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(bookingId));
        Assert.Equal((30m, 30m), (item.UnitPrice, item.Amount));
        BookingDto dto = (await w.Appointments.GetById(w.OrganizationId, created.Id)).Bookings.Single();
        Assert.Equal((30m, 10m, 20m, false), (dto.Amount, dto.PaidAmount, dto.OutstandingAmount, dto.IsPaid));
    }

    #endregion
}
