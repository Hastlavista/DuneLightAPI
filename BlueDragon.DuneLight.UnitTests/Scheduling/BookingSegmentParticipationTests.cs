#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D2 — BookingSegmentParticipation persistence foundation: a Booking's client participating in one segment of the
/// SAME appointment, with lifecycle status/version, arrival and cancellation metadata and a pricing snapshot.
/// D3B1: every production Booking now owns exactly one (authoritative) participation on its appointment's segment, created
/// by BookingFactory (D3B2: carrying the booking's authoritative price). These foundation tests therefore attach their
/// EXTRA participations (IBookingSegmentParticipationHandler.Add) to additional segments, and ParticipationCount counts
/// only those extra rows (all participations minus the one production participation per booking).
/// </summary>
public class BookingSegmentParticipationTests
{
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);

    private static IBookingSegmentParticipationHandler Participations(SchedulingWorld w) => w.Resolve<IBookingSegmentParticipationHandler>();

    /// <summary>An individual appointment (through the real Create flow) with its Booking(s); segment #1 is its production
    /// segment (D3A), the rest are added directly.</summary>
    private sealed record Setup(Guid AppointmentId, List<Booking> Bookings, List<AppointmentSegment> Segments);

    private static async Task<Setup> AppointmentWithSegments(SchedulingWorld w, int hour, int segments = 1, params Client[] extraClients)
    {
        AppointmentDto dto = await w.CreateAppointment(Z(hour), extraClients: extraClients);
        // D3A: segment #1 is the appointment's own production (authoritative) segment; further ones are added here.
        Appointment created0 = await w.LoadAppointment(dto.Id);
        List<AppointmentSegment> created = new() { Assert.Single(created0.Segments) };
        for (int i = 1; i < segments; i++)
        {
            AppointmentSegment segment = new()
            {
                Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, AppointmentId = dto.Id, ServiceId = w.Service.Id.Value,
                PlannedStart = Z(hour, i * 15), PlannedEnd = Z(hour, i * 15 + 15), CreatedAt = DateTimeOffset.UtcNow
            };
            await w.Resolve<IAppointmentSegmentHandler>().Add(segment);
            created.Add(segment);
        }

        Appointment appointment = await w.LoadAppointment(dto.Id);
        return new Setup(dto.Id, appointment.Bookings.OrderBy(b => b.ClientId == w.Client.Id ? 0 : 1).ToList(), created);
    }

    private static BookingSegmentParticipation Participation(SchedulingWorld w, Booking booking, AppointmentSegment segment,
        ParticipationStatus status = ParticipationStatus.Confirmed) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = w.OrganizationId,
        BookingId = booking.Id.Value,
        AppointmentSegmentId = segment.Id.Value,
        Status = status,
        BaseAmount = 50m,
        BaseAmountSource = PriceSource.Default,
        SuggestedAmount = 50m,
        Amount = 50m,
        IsAmountManuallyOverridden = false, // D3B1: nullable (snapshot optional until D3B2) — a full snapshot sets it
        CreatedAt = DateTimeOffset.UtcNow
    };

    /// <summary>Participations added by these tests: every production booking owns exactly one, the rest are extra.</summary>
    private static async Task<int> ParticipationCount(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.BookingSegmentParticipations.CountAsync(p => p.OrganizationId == w.OrganizationId)
               - await db.Bookings.CountAsync(b => b.OrganizationId == w.OrganizationId);
    }

    /// <summary>Gives the production participation of the booking lifecycle history without changing its status
    /// (StatusVersion &gt; 0 — e.g. Confirmed -&gt; Completed -&gt; Confirmed).</summary>
    private static async Task GiveHistory(SchedulingWorld w, Guid bookingId)
    {
        await using DatabaseContext db = w.NewDb();
        await db.BookingSegmentParticipations.Where(p => p.BookingId == bookingId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.StatusVersion, 2));
    }

    #region Basic persistence and relations

    [Fact]
    public async Task Participation_IsPersistedAndRead_WithItsBookingSegmentAndOrganization()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Participation_IsPersistedAndRead_WithItsBookingSegmentAndOrganization));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);

        await Participations(w).Add(p);

        BookingSegmentParticipation read = await Participations(w).GetById(w.OrganizationId, p.Id.Value);
        Assert.Equal(w.OrganizationId, read.OrganizationId);
        Assert.Equal(s.Bookings[0].Id, read.BookingId);
        Assert.Equal(s.Segments[1].Id, read.AppointmentSegmentId);
        Assert.Equal(ParticipationStatus.Confirmed, read.Status);

        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation withGraph = await db.BookingSegmentParticipations
            .Include(x => x.Booking).Include(x => x.Segment)
            .SingleAsync(x => x.Id == p.Id);
        Assert.Equal(w.Client.Id, withGraph.Booking.ClientId);
        Assert.Equal(s.AppointmentId, withGraph.Segment.AppointmentId);
        Booking booking = await db.Bookings.Include(b => b.Participations).SingleAsync(b => b.Id == s.Bookings[0].Id);
        Assert.Equal(2, booking.Participations.Count); // its production participation + this one
        Assert.Contains(booking.Participations, x => x.Id == p.Id);
        AppointmentSegment segment = await db.AppointmentSegments.Include(x => x.Participations).SingleAsync(x => x.Id == s.Segments[1].Id);
        Assert.Equal(p.Id, Assert.Single(segment.Participations).Id);
    }

    [Fact]
    public async Task UnknownBookingOrSegment_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(UnknownBookingOrSegment_IsRejected));
        Setup s = await AppointmentWithSegments(w, 10);

        BookingSegmentParticipation noBooking = Participation(w, s.Bookings[0], s.Segments[0]);
        noBooking.BookingId = Guid.NewGuid();
        BookingSegmentParticipation noSegment = Participation(w, s.Bookings[0], s.Segments[0]);
        noSegment.AppointmentSegmentId = Guid.NewGuid();

        await SchedulingAssert.NotFound(() => Participations(w).Add(noBooking));
        await SchedulingAssert.NotFound(() => Participations(w).Add(noSegment));
        Assert.Equal(0, await ParticipationCount(w));
    }

    [Fact]
    public async Task Database_RequiresExistingBookingAndSegment_EvenOutsideTheHandler()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_RequiresExistingBookingAndSegment_EvenOutsideTheHandler));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);

        async Task<string> Fails(Action<BookingSegmentParticipation> mutate)
        {
            BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);
            mutate(p);
            await using DatabaseContext db = w.NewDb();
            db.BookingSegmentParticipations.Add(p);
            return (await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException?.Message;
        }

        Assert.Contains("fk_booking_segment_participations_booking_id", await Fails(p => p.BookingId = Guid.NewGuid()));
        Assert.Contains("fk_booking_segment_participations_appointment_segment_id", await Fails(p => p.AppointmentSegmentId = Guid.NewGuid()));
        Assert.Contains("fk_booking_segment_participations_organization_id", await Fails(p => p.OrganizationId = Guid.NewGuid()));
    }

    [Fact]
    public async Task Reads_AreScopedToTheOrganization()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Reads_AreScopedToTheOrganization));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(Reads_AreScopedToTheOrganization)}-other");
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);
        await Participations(w).Add(p);

        Assert.Null(await Participations(w).GetById(other.OrganizationId, p.Id.Value));
        Assert.Empty(await Participations(w).GetForBooking(other.OrganizationId, s.Bookings[0].Id.Value));
        Assert.Empty(await Participations(w).GetForSegment(other.OrganizationId, s.Segments[0].Id.Value));
        Assert.False(await Participations(w).ExistsForAppointment(other.OrganizationId, s.AppointmentId));
        Assert.True(await Participations(w).ExistsForAppointment(w.OrganizationId, s.AppointmentId));
    }

    #endregion

    #region Same-appointment and organization invariants

    [Fact]
    public async Task BookingAndSegmentOfTheSameAppointment_AreAccepted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingAndSegmentOfTheSameAppointment_AreAccepted));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);

        await Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[1]));

        Assert.Equal(1, await ParticipationCount(w));
    }

    [Fact]
    public async Task BookingOfAppointmentA_WithSegmentOfAppointmentB_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingOfAppointmentA_WithSegmentOfAppointmentB_IsRejected));
        Setup a = await AppointmentWithSegments(w, 10);
        Setup b = await AppointmentWithSegments(w, 12);

        await SchedulingAssert.BusinessRule(ErrorCodes.ParticipationAppointmentMismatch,
            () => Participations(w).Add(Participation(w, a.Bookings[0], b.Segments[0])));
        await SchedulingAssert.BusinessRule(ErrorCodes.ParticipationAppointmentMismatch,
            () => Participations(w).Add(Participation(w, b.Bookings[0], a.Segments[0])));

        Assert.Equal(0, await ParticipationCount(w));
    }

    [Fact]
    public async Task CrossOrganizationPairings_AreRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CrossOrganizationPairings_AreRejected));
        await using SchedulingWorld other = await SchedulingWorld.Create($"{nameof(CrossOrganizationPairings_AreRejected)}-other");
        Setup mine = await AppointmentWithSegments(w, 10);
        Setup theirs = await AppointmentWithSegments(other, 10);

        // Participation claims this organization but points at the other organization's booking / segment / both.
        await SchedulingAssert.NotFound(() => Participations(w).Add(Participation(w, theirs.Bookings[0], mine.Segments[0])));
        await SchedulingAssert.NotFound(() => Participations(w).Add(Participation(w, mine.Bookings[0], theirs.Segments[0])));
        await SchedulingAssert.NotFound(() => Participations(w).Add(Participation(w, theirs.Bookings[0], theirs.Segments[0])));

        // Participation claims the other organization for this organization's pair.
        BookingSegmentParticipation foreign = Participation(w, mine.Bookings[0], mine.Segments[0]);
        foreign.OrganizationId = other.OrganizationId;
        await SchedulingAssert.NotFound(() => Participations(w).Add(foreign));

        Assert.Equal(0, await ParticipationCount(w));
        Assert.Equal(0, await ParticipationCount(other));
    }

    #endregion

    #region Uniqueness

    [Fact]
    public async Task SameBookingAndSegment_Twice_IsRejected_InTheHandlerAndInTheDatabase()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameBookingAndSegment_Twice_IsRejected_InTheHandlerAndInTheDatabase));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        await Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[1]));

        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateParticipation,
            () => Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[1], ParticipationStatus.Cancelled)));
        // D3B1: a second participation beside the booking's production one on the same segment is a duplicate too.
        await SchedulingAssert.BusinessRule(ErrorCodes.DuplicateParticipation,
            () => Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[0])));

        await using DatabaseContext db = w.NewDb();
        db.BookingSegmentParticipations.Add(Participation(w, s.Bookings[0], s.Segments[1]));
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ux_booking_segment_participations_booking_segment", ex.InnerException?.Message);
        Assert.Equal(1, await ParticipationCount(w));
    }

    [Fact]
    public async Task OneBooking_InSeveralSegments_AndOneSegment_WithSeveralBookings_AreAccepted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OneBooking_InSeveralSegments_AndOneSegment_WithSeveralBookings_AreAccepted));
        Client second = await w.AddClient("Second", "Client");
        Client third = await w.AddClient("Third", "Client");
        Setup s = await AppointmentWithSegments(w, 10, segments: 4, second, third);
        Assert.Equal(3, s.Bookings.Count); // still one Booking per client on the appointment

        // Segment #0 already holds every booking's production participation (D3B1); the extra ones go to #1..#3.
        foreach (AppointmentSegment segment in s.Segments.Skip(1))
            await Participations(w).Add(Participation(w, s.Bookings[0], segment));
        foreach (Booking booking in s.Bookings.Skip(1))
            await Participations(w).Add(Participation(w, booking, s.Segments[1]));

        Assert.Equal(s.Segments.Select(x => x.Id.Value), (await Participations(w).GetForBooking(w.OrganizationId, s.Bookings[0].Id.Value)).Select(p => p.AppointmentSegmentId));
        Assert.Equal(3, (await Participations(w).GetForSegment(w.OrganizationId, s.Segments[1].Id.Value)).Count);
        Assert.Equal(3, (await Participations(w).GetForSegment(w.OrganizationId, s.Segments[0].Id.Value)).Count);
        Assert.Equal(5, await ParticipationCount(w));
    }

    #endregion

    #region Status, StatusVersion, arrival, cancellation

    [Theory]
    [InlineData(ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Completed)]
    [InlineData(ParticipationStatus.Cancelled)]
    [InlineData(ParticipationStatus.NoShow)]
    public async Task EveryStatus_RoundTrips_AndNewParticipationsStartAtStatusVersionZero(ParticipationStatus status)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(EveryStatus_RoundTrips_AndNewParticipationsStartAtStatusVersionZero)}-{status}");
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1], status);
        p.StatusVersion = 7; // creation is not a transition: the write path always starts at 0

        await Participations(w).Add(p);

        BookingSegmentParticipation read = await Participations(w).GetById(w.OrganizationId, p.Id.Value);
        Assert.Equal(status, read.Status);
        Assert.Equal(0, read.StatusVersion);
    }

    [Fact]
    public void StatusType_HasExactlyTheFourLifecycleStates()
    {
        Assert.Equal(new[] { "Confirmed", "Completed", "Cancelled", "NoShow" }, Enum.GetNames<ParticipationStatus>());
    }

    [Theory]
    [InlineData("Arrived")]
    [InlineData("LateCancelled")]
    [InlineData("Mixed")]
    public async Task Database_RejectsAnyOtherStatus(string status)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(Database_RejectsAnyOtherStatus)}-{status}");
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        await Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[1]));

        await using DatabaseContext db = w.NewDb();
        PostgresExceptionHolder error = await PostgresExceptionHolder.Capture(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE dunelight.booking_segment_participations SET status = {0} WHERE organization_id = {1}", status, w.OrganizationId));
        Assert.Contains("ck_booking_segment_participations_status", error.Message);
    }

    [Fact]
    public async Task StatusVersion_IsPersisted_AndCannotBeNegative()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(StatusVersion_IsPersisted_AndCannotBeNegative));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);
        await Participations(w).Add(p);

        await using (DatabaseContext db = w.NewDb())
        {
            BookingSegmentParticipation tracked = await db.BookingSegmentParticipations.SingleAsync(x => x.Id == p.Id);
            tracked.Status = ParticipationStatus.NoShow;
            tracked.StatusVersion = 3;
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, (await Participations(w).GetById(w.OrganizationId, p.Id.Value)).StatusVersion);

        await using DatabaseContext db2 = w.NewDb();
        BookingSegmentParticipation again = await db2.BookingSegmentParticipations.SingleAsync(x => x.Id == p.Id);
        again.StatusVersion = -1;
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Contains("ck_booking_segment_participations_status_version", ex.InnerException?.Message);
    }

    [Fact]
    public async Task Arrival_IsOptionalMetadata_IndependentOfStatus()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Arrival_IsOptionalMetadata_IndependentOfStatus));
        Setup s = await AppointmentWithSegments(w, 10, segments: 3);
        BookingSegmentParticipation notArrived = Participation(w, s.Bookings[0], s.Segments[1]);
        BookingSegmentParticipation arrived = Participation(w, s.Bookings[0], s.Segments[2]); // still Confirmed
        arrived.ArrivedAt = new DateTimeOffset(2031, 3, 3, 11, 58, 0, TimeSpan.FromHours(2));
        arrived.ArrivedBy = w.ActorUserId;

        await Participations(w).Add(notArrived);
        await Participations(w).Add(arrived);

        BookingSegmentParticipation readNot = await Participations(w).GetById(w.OrganizationId, notArrived.Id.Value);
        Assert.Null(readNot.ArrivedAt);
        Assert.Null(readNot.ArrivedBy);
        BookingSegmentParticipation read = await Participations(w).GetById(w.OrganizationId, arrived.Id.Value);
        Assert.Equal(ParticipationStatus.Confirmed, read.Status);
        Assert.Equal(new DateTimeOffset(2031, 3, 3, 9, 58, 0, TimeSpan.Zero), read.ArrivedAt);
        Assert.Equal(TimeSpan.Zero, read.ArrivedAt.Value.Offset);
        Assert.Equal(w.ActorUserId, read.ArrivedBy);
    }

    [Fact]
    public async Task ArrivedBy_WithoutArrivedAt_IsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ArrivedBy_WithoutArrivedAt_IsRejected));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);
        p.ArrivedBy = w.ActorUserId;

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => Participations(w).Add(p));

        Assert.Contains("ck_booking_segment_participations_arrival", ex.InnerException?.Message);
    }

    [Fact]
    public async Task Cancellation_KeepsReasonAndLateClassification_AsMetadataBesideTheCancelledStatus()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Cancellation_KeepsReasonAndLateClassification_AsMetadataBesideTheCancelledStatus));
        Setup s = await AppointmentWithSegments(w, 10, segments: 4);
        BookingSegmentParticipation late = Participation(w, s.Bookings[0], s.Segments[3], ParticipationStatus.Cancelled);
        late.CancellationReason = "sick";
        late.IsLateCancellation = true;
        BookingSegmentParticipation inTime = Participation(w, s.Bookings[0], s.Segments[1], ParticipationStatus.Cancelled);
        inTime.CancellationReason = "travel";
        inTime.IsLateCancellation = false;
        BookingSegmentParticipation noShow = Participation(w, s.Bookings[0], s.Segments[2], ParticipationStatus.NoShow);
        noShow.CancellationReason = "did not come"; // late classification stays null for a no-show, as on Booking

        foreach (BookingSegmentParticipation p in new[] { late, inTime, noShow })
            await Participations(w).Add(p);

        BookingSegmentParticipation readLate = await Participations(w).GetById(w.OrganizationId, late.Id.Value);
        Assert.Equal(ParticipationStatus.Cancelled, readLate.Status);
        Assert.True(readLate.IsLateCancellation);
        Assert.Equal("sick", readLate.CancellationReason);
        BookingSegmentParticipation readInTime = await Participations(w).GetById(w.OrganizationId, inTime.Id.Value);
        Assert.Equal(ParticipationStatus.Cancelled, readInTime.Status);
        Assert.False(readInTime.IsLateCancellation);
        BookingSegmentParticipation readNoShow = await Participations(w).GetById(w.OrganizationId, noShow.Id.Value);
        Assert.Equal(ParticipationStatus.NoShow, readNoShow.Status);
        Assert.Null(readNoShow.IsLateCancellation);
        Assert.Equal("did not come", readNoShow.CancellationReason);
    }

    #endregion

    #region Pricing snapshot

    [Fact]
    public async Task PricingSnapshot_RoundTrips_WithAndWithoutAdjustment_AndManualOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PricingSnapshot_RoundTrips_WithAndWithoutAdjustment_AndManualOverride));
        Setup s = await AppointmentWithSegments(w, 10, segments: 3);
        BookingSegmentParticipation plain = Participation(w, s.Bookings[0], s.Segments[2]);
        plain.BaseAmount = 40m;
        plain.BaseAmountSource = PriceSource.CompanySpecific;
        plain.SuggestedAmount = 40m;
        plain.Amount = 40m;
        BookingSegmentParticipation adjusted = Participation(w, s.Bookings[0], s.Segments[1]);
        adjusted.BaseAmount = 50m;
        adjusted.BaseAmountSource = PriceSource.AllCompanies;
        adjusted.AdjustmentAmount = -7.5m;
        adjusted.SuggestedAmount = 42.5m;
        adjusted.Amount = 30m;
        adjusted.IsAmountManuallyOverridden = true;

        await Participations(w).Add(plain);
        await Participations(w).Add(adjusted);

        BookingSegmentParticipation readPlain = await Participations(w).GetById(w.OrganizationId, plain.Id.Value);
        Assert.Equal(((decimal?)40m, (PriceSource?)PriceSource.CompanySpecific, (decimal?)null, (decimal?)40m, (decimal?)40m, (bool?)false),
            (readPlain.BaseAmount, readPlain.BaseAmountSource, readPlain.AdjustmentAmount, readPlain.SuggestedAmount, readPlain.Amount, readPlain.IsAmountManuallyOverridden));
        BookingSegmentParticipation readAdjusted = await Participations(w).GetById(w.OrganizationId, adjusted.Id.Value);
        Assert.Equal(((decimal?)50m, (PriceSource?)PriceSource.AllCompanies, (decimal?)-7.5m, (decimal?)42.5m, (decimal?)30m, (bool?)true),
            (readAdjusted.BaseAmount, readAdjusted.BaseAmountSource, readAdjusted.AdjustmentAmount, readAdjusted.SuggestedAmount, readAdjusted.Amount, readAdjusted.IsAmountManuallyOverridden));
    }

    [Fact]
    public async Task PricingSnapshot_RejectsNegativeAmounts()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PricingSnapshot_RejectsNegativeAmounts));
        Setup s = await AppointmentWithSegments(w, 10, segments: 2);
        BookingSegmentParticipation p = Participation(w, s.Bookings[0], s.Segments[1]);
        p.Amount = -1m;

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => Participations(w).Add(p));

        Assert.Contains("ck_booking_segment_participations_amounts_non_negative", ex.InnerException?.Message);
    }

    #endregion

    #region Delete / history

    [Fact]
    public async Task Segment_WithoutParticipation_CanBeDeleted_ButNotOnceItHasOne()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Segment_WithoutParticipation_CanBeDeleted_ButNotOnceItHasOne));
        Setup s = await AppointmentWithSegments(w, 10, segments: 3);
        await Participations(w).Add(Participation(w, s.Bookings[0], s.Segments[1]));

        await using (DatabaseContext db = w.NewDb())
        {
            db.AppointmentSegments.Remove(await db.AppointmentSegments.SingleAsync(x => x.Id == s.Segments[2].Id));
            await db.SaveChangesAsync();
        }

        await using (DatabaseContext db = w.NewDb())
        {
            db.AppointmentSegments.Remove(await db.AppointmentSegments.SingleAsync(x => x.Id == s.Segments[1].Id));
            DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("fk_booking_segment_participations_appointment_segment_id", ex.InnerException?.Message);
        }

        await using DatabaseContext verify = w.NewDb();
        Assert.Equal(new[] { s.Segments[0].Id, s.Segments[1].Id }.OrderBy(x => x),
            (await verify.AppointmentSegments.Where(x => x.AppointmentId == s.AppointmentId).Select(x => x.Id).ToArrayAsync()).OrderBy(x => x));
        Assert.Equal(1, await ParticipationCount(w));
    }

    [Fact]
    public async Task Booking_WithParticipation_CannotBeDeletedDirectly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Booking_WithParticipation_CannotBeDeletedDirectly));
        Setup s = await AppointmentWithSegments(w, 10); // D3B1: the booking's production participation is enough

        await using DatabaseContext db = w.NewDb();
        db.Bookings.Remove(await db.Bookings.IgnoreAutoIncludes().SingleAsync(b => b.Id == s.Bookings[0].Id));
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Contains("fk_booking_segment_participations_booking_id", ex.InnerException?.Message);
    }

    [Fact]
    public async Task RemovingAClientWithParticipationHistory_ThroughUpdate_IsBlocked_AndNothingIsErased()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemovingAClientWithParticipationHistory_ThroughUpdate_IsBlocked_AndNothingIsErased));
        Client second = await w.AddClient("Second", "Client");
        Setup s = await AppointmentWithSegments(w, 10, 1, second);
        Booking secondBooking = s.Bookings.Single(b => b.ClientId == second.Id);
        await GiveHistory(w, secondBooking.Id.Value); // D3B1: history (not mere existence) blocks the removal
        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, s.AppointmentId);

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, s.AppointmentId, w.UpdateRequest(current, r => r.ClientIds = new List<Guid> { w.Client.Id.Value })));

        Appointment after = await w.LoadAppointment(s.AppointmentId);
        Assert.Equal(2, after.Bookings.Count);
        await using DatabaseContext verify = w.NewDb();
        Assert.Equal(2, await verify.BookingSegmentParticipations.CountAsync(p => p.Segment.AppointmentId == s.AppointmentId));
    }

    [Fact]
    public async Task RemovingAClientWithAnUntouchedParticipation_ThroughUpdate_StillHardDeletesTheConfirmedBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RemovingAClientWithAnUntouchedParticipation_ThroughUpdate_StillHardDeletesTheConfirmedBooking));
        Client second = await w.AddClient("Second", "Client");
        Setup s = await AppointmentWithSegments(w, 10, 1, second);
        await GiveHistory(w, s.Bookings.Single(b => b.ClientId == w.Client.Id).Id.Value); // the KEPT client's history is irrelevant
        AppointmentDto current = await w.Appointments.GetById(w.OrganizationId, s.AppointmentId);

        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, s.AppointmentId,
            w.UpdateRequest(current, r => r.ClientIds = new List<Guid> { w.Client.Id.Value }));

        Appointment after = await w.LoadAppointment(s.AppointmentId);
        Assert.Equal(new[] { w.Client.Id.Value }, after.Bookings.Select(b => b.ClientId).ToArray());
        await using DatabaseContext verify = w.NewDb();
        Assert.Equal(1, await verify.BookingSegmentParticipations.CountAsync(p => p.Segment.AppointmentId == s.AppointmentId));
    }

    [Fact]
    public async Task DeletingAnAppointment_WithParticipationHistory_IsBlocked_AndNothingIsErased()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DeletingAnAppointment_WithParticipationHistory_IsBlocked_AndNothingIsErased));
        Setup s = await AppointmentWithSegments(w, 10);
        await GiveHistory(w, s.Bookings[0].Id.Value);

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, s.AppointmentId));

        // Even bypassing the service, the database refuses to cascade the history away.
        await using (DatabaseContext db = w.NewDb())
        {
            db.Appointments.Remove(await db.Appointments.IgnoreAutoIncludes().SingleAsync(a => a.Id == s.AppointmentId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using DatabaseContext verify = w.NewDb();
        Assert.True(await verify.Appointments.AnyAsync(a => a.Id == s.AppointmentId));
        Assert.True(await verify.Bookings.AnyAsync(b => b.Id == s.Bookings[0].Id));
        Assert.True(await verify.AppointmentSegments.AnyAsync(x => x.Id == s.Segments[0].Id));
        Assert.True(await verify.BookingSegmentParticipations.AnyAsync(p => p.BookingId == s.Bookings[0].Id));
    }

    [Fact]
    public async Task DeletingAnAppointment_WithOnlyUntouchedParticipations_IsUnchanged_AndRemovesThem()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(DeletingAnAppointment_WithOnlyUntouchedParticipations_IsUnchanged_AndRemovesThem));
        Setup withSegment = await AppointmentWithSegments(w, 10);
        AppointmentDto plain = await w.CreateAppointment(Z(14));

        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, withSegment.AppointmentId);
        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, plain.Id);

        await using DatabaseContext verify = w.NewDb();
        Assert.False(await verify.Appointments.AnyAsync(a => a.Id == withSegment.AppointmentId || a.Id == plain.Id));
        Assert.False(await verify.Bookings.AnyAsync(b => b.AppointmentId == withSegment.AppointmentId || b.AppointmentId == plain.Id));
        Assert.False(await verify.AppointmentSegments.AnyAsync(x => x.AppointmentId == withSegment.AppointmentId));
        Assert.False(await verify.BookingSegmentParticipations.AnyAsync(p => p.OrganizationId == w.OrganizationId));
    }

    #endregion

    #region D3B1: production flows create exactly one participation per booking

    [Fact]
    public async Task ProductionFlows_CreateExactlyOneParticipationPerBooking_OnTheAppointmentsSegment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ProductionFlows_CreateExactlyOneParticipationPerBooking_OnTheAppointmentsSegment));

        // Individual: create, recurring, AddBooking, SetStatus, Complete (existing and new).
        AppointmentDto single = await w.CreateAppointment(Z(8));
        await w.Appointments.CreateRecurring(w.OrganizationId, w.ActorUserId, true, new RecurringAppointmentCreateRequest
        {
            RecurrenceType = RecurrenceType.Weekly, ServiceId = w.Service.Id.Value, EmployeeId = w.Employee.Id.Value,
            CompanyId = w.Company.Id.Value, ClientIds = new List<Guid> { w.Client.Id.Value },
            FirstOccurrenceStartsAt = Z(9), EndDate = Z(9).AddDays(14)
        });
        AppointmentDto toCancel = await w.CreateAppointment(Z(11));
        await w.SetBookingStatus(toCancel.Id, w.Client, BookingStatus.Cancelled, "changed plans");
        await w.CompleteExisting(single.Id, w.CompleteRequest(Z(8)));
        await w.CompleteNew(w.CompleteRequest(Z(13)));

        // Group: generation, AddMember, AddBooking (guest), waitlist promotion, attendance via SetStatus.
        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 1);
        Client member = await w.AddClient("Member", "Client");
        await w.AddGroupMember(group, member);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Client waiter = await w.AddClient("Waiter", "Client");
        await w.Waitlist.Join(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new WaitlistJoinRequest { ClientId = waiter.Id.Value });
        await w.SetBookingStatus(occurrence.Id.Value, member, BookingStatus.Cancelled, "cannot come"); // promotes the waiter
        await w.SetBookingStatus(occurrence.Id.Value, waiter, BookingStatus.Completed);
        Client guest = await w.AddClient("Guest", "Client");
        await w.AddGuest(occurrence, guest);
        // AddMember after generation syncs a Booking onto the existing future occurrence.
        GroupDto roomy = await w.CreateGroup(groupService, capacity: 5, slots: (DayOfWeek.Monday, TimeSpan.FromHours(16)));
        Appointment roomyOccurrence = await w.GenerateSingleOccurrence(roomy);
        Client lateMember = await w.AddClient("Late", "Member");
        await w.AddGroupMember(roomy, lateMember);

        await using DatabaseContext db = w.NewDb();
        Assert.True(await db.Bookings.CountAsync(b => b.OrganizationId == w.OrganizationId) >= 9);
        Assert.Contains(await db.Bookings.Where(b => b.AppointmentId == occurrence.Id).Select(b => b.ClientId).ToListAsync(), c => c == waiter.Id);
        Assert.Contains(await db.Bookings.Where(b => b.AppointmentId == roomyOccurrence.Id).Select(b => b.ClientId).ToListAsync(), c => c == lateMember.Id);
        Assert.Equal(0, await ParticipationCount(w)); // no participation beyond the one per booking
        // D3B2: every participation carries its booking's price (the Booking has none).
        Assert.False(await db.BookingSegmentParticipations.AnyAsync(p => p.OrganizationId == w.OrganizationId && p.SuggestedAmount <= 0m));
        // D3A: every appointment has exactly one (production) segment; D3B1: every booking has exactly one participation on it.
        List<Guid> appointmentIds = await db.Appointments.Where(a => a.OrganizationId == w.OrganizationId).Select(a => a.Id.Value).ToListAsync();
        Assert.All(appointmentIds, id => Assert.Equal(1, db.AppointmentSegments.Count(x => x.AppointmentId == id)));
        var pairs = await db.Bookings.IgnoreAutoIncludes().Where(b => b.OrganizationId == w.OrganizationId)
            .Select(b => new { b.Id, b.AppointmentId, Segments = b.Participations.Select(p => p.Segment.AppointmentId).ToList() })
            .ToListAsync();
        Assert.All(pairs, b => Assert.Equal(b.AppointmentId, Assert.Single(b.Segments)));
        Assert.Equal(pairs.Count, await db.BookingSegmentParticipations.CountAsync(p => p.OrganizationId == w.OrganizationId));
    }

    #endregion
}

/// <summary>Captures the PostgreSQL error raised by a raw SQL statement.</summary>
internal sealed record PostgresExceptionHolder(string Message)
{
    public static async Task<PostgresExceptionHolder> Capture(Func<Task> action)
    {
        Npgsql.PostgresException ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(action);
        return new PostgresExceptionHolder(ex.ConstraintName + " " + ex.MessageText);
    }
}
