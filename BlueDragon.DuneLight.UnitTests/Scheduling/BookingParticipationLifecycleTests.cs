#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B1 — BookingSegmentParticipation is the ONLY store of a booking's execution lifecycle (status, StatusVersion,
/// cancellation reason, late classification); Booking keeps identity and money. Covers: the creation seam, lifecycle
/// writes, the single-participation resolver's failure modes, the read model, the dropped booking columns, and the locked
/// deletion rule — a Booking + Participation may be hard-deleted only while the Participation is UNTOUCHED (Confirmed,
/// StatusVersion 0, no arrival, no cancellation metadata), for Update / CompleteExisting omission and same-day Delete.
/// </summary>
public class BookingParticipationLifecycleTests
{
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);

    private static async Task<BookingSegmentParticipation> ParticipationOf(SchedulingWorld w, Guid appointmentId, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.BookingSegmentParticipations.AsNoTracking().Include(p => p.Segment)
            .SingleAsync(p => p.Booking.AppointmentId == appointmentId && p.Booking.ClientId == client.Id);
    }

    private static async Task Mutate(SchedulingWorld w, Guid appointmentId, Client client, Action<BookingSegmentParticipation> mutate)
    {
        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation tracked = await db.BookingSegmentParticipations
            .SingleAsync(p => p.Booking.AppointmentId == appointmentId && p.Booking.ClientId == client.Id);
        mutate(tracked);
        await db.SaveChangesAsync();
    }

    private static async Task<(int Bookings, int Participations)> Rows(SchedulingWorld w, Guid appointmentId, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        return (await db.Bookings.CountAsync(b => b.AppointmentId == appointmentId && b.ClientId == client.Id),
            await db.BookingSegmentParticipations.CountAsync(p => p.Booking.AppointmentId == appointmentId && p.Booking.ClientId == client.Id));
    }

    private static Task UpdateOmitting(SchedulingWorld w, Guid appointmentId, Client kept) =>
        w.Appointments.GetById(w.OrganizationId, appointmentId).ContinueWith(t => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, appointmentId,
            w.UpdateRequest(t.Result, r => r.ClientIds = new List<Guid> { kept.Id.Value }))).Unwrap();

    #region Creation seam

    [Fact]
    public async Task Create_PairsTheBookingWithOneUntouchedParticipationOnTheSegment_CarryingItsPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Create_PairsTheBookingWithOneUntouchedParticipationOnTheSegment_CarryingItsPrice));

        AppointmentDto created = await w.CreateAppointment(Z(10));

        BookingSegmentParticipation p = await ParticipationOf(w, created.Id, w.Client);
        Assert.Equal(created.Id, p.Segment.AppointmentId);
        Assert.Equal(w.OrganizationId, p.OrganizationId);
        Assert.Equal(ParticipationStatus.Confirmed, p.Status);
        Assert.Equal(0, p.StatusVersion);
        Assert.True(ParticipationHistory.IsUntouched(p));
        // D3B2: the price is the participation's — and pricing alone is not lifecycle history (still untouched above).
        Assert.Equal(created.Bookings.Single().Amount, p.Amount);
        Assert.Equal(created.Bookings.Single().SuggestedAmount, p.SuggestedAmount);
    }

    [Fact]
    public async Task Update_AddingAClient_CreatesItsBookingAndExactlyOneParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_AddingAClient_CreatesItsBookingAndExactlyOneParticipation));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10));

        await w.Appointments.Update(w.OrganizationId, w.ActorUserId, true, created.Id,
            w.UpdateRequest(created, r => r.ClientIds = new List<Guid> { w.Client.Id.Value, second.Id.Value }));

        Assert.Equal((1, 1), await Rows(w, created.Id, second));
        BookingSegmentParticipation p = await ParticipationOf(w, created.Id, second);
        Assert.Equal(created.Id, p.Segment.AppointmentId);
        Assert.True(ParticipationHistory.IsUntouched(p));
    }

    [Fact]
    public async Task CompleteNew_CreatesTheParticipationAlreadyCompleted()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNew_CreatesTheParticipationAlreadyCompleted));

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10)));

        BookingSegmentParticipation p = await ParticipationOf(w, dto.Id, w.Client);
        Assert.Equal(ParticipationStatus.Completed, p.Status);
        Assert.Equal(0, p.StatusVersion); // creation is not a transition (unchanged semantics)
    }

    #endregion

    #region Lifecycle writes and the read model

    [Fact]
    public async Task Cancellation_IsWrittenToTheParticipation_AndTheApiDerivesItFromThere()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Cancellation_IsWrittenToTheParticipation_AndTheApiDerivesItFromThere));
        AppointmentDto created = await w.CreateAppointment(Z(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "changed plans");

        BookingSegmentParticipation p = await ParticipationOf(w, created.Id, w.Client);
        Assert.Equal(ParticipationStatus.Cancelled, p.Status);
        Assert.Equal(1, p.StatusVersion);
        Assert.Equal("changed plans", p.CancellationReason);
        Assert.False(p.IsLateCancellation); // far-future start: classified, not late
        Assert.NotNull(p.UpdatedAt);

        AppointmentDto read = await w.Appointments.GetById(w.OrganizationId, created.Id);
        BookingDto booking = read.Bookings.Single();
        Assert.Equal(BookingStatusSummary.Cancelled, booking.Status);
        Assert.Equal("changed plans", booking.CancellationReason);
    }

    [Fact]
    public async Task Correction_CompletedToConfirmed_AdvancesTheParticipationVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_CompletedToConfirmed_AdvancesTheParticipationVersion));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), paymentMethod: PaymentMethod.Cash));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);

        BookingSegmentParticipation p = await ParticipationOf(w, created.Id, w.Client);
        Assert.Equal(ParticipationStatus.Confirmed, p.Status);
        Assert.Equal(2, p.StatusVersion);
        Assert.False(ParticipationHistory.IsUntouched(p));
    }

    [Fact]
    public async Task BookingTable_NoLongerHasLifecycleColumns()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingTable_NoLongerHasLifecycleColumns));
        await using DatabaseContext db = w.NewDb();

        List<string> columns = await db.Database.SqlQueryRaw<string>(@"
            SELECT column_name AS ""Value"" FROM information_schema.columns
             WHERE table_schema = 'dunelight' AND table_name = 'bookings'
               AND column_name IN ('status', 'status_version', 'cancellation_reason', 'is_late_cancellation')").ToListAsync();

        Assert.Empty(columns);
    }

    #endregion

    #region Single-participation resolver

    [Fact]
    public async Task ABookingWithoutAParticipation_FailsExplicitly_OnReadAndWrite()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ABookingWithoutAParticipation_FailsExplicitly_OnReadAndWrite));
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await using (DatabaseContext db = w.NewDb())
            await db.BookingSegmentParticipations.Where(p => p.Booking.AppointmentId == created.Id).ExecuteDeleteAsync();

        await Assert.ThrowsAsync<InvalidBookingParticipationStateException>(() => w.Appointments.GetById(w.OrganizationId, created.Id));
        await Assert.ThrowsAsync<InvalidBookingParticipationStateException>(
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "x"));
    }

    [Fact]
    public void Resolver_RejectsZeroSeveralForeignAndCrossAppointmentParticipations()
    {
        Guid org = Guid.NewGuid();
        Guid appointmentId = Guid.NewGuid();
        AppointmentSegment own = new() { Id = Guid.NewGuid(), OrganizationId = org, AppointmentId = appointmentId };
        AppointmentSegment foreignSegment = new() { Id = Guid.NewGuid(), OrganizationId = org, AppointmentId = Guid.NewGuid() };

        Booking Booking(params Func<Guid, BookingSegmentParticipation>[] participations)
        {
            Booking b = new() { Id = Guid.NewGuid(), OrganizationId = org, AppointmentId = appointmentId };
            foreach (Func<Guid, BookingSegmentParticipation> p in participations)
                b.Participations.Add(p(b.Id.Value));
            return b;
        }

        BookingSegmentParticipation On(Guid bookingId, AppointmentSegment segment, Guid? organizationId = null) => new()
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId ?? org, BookingId = bookingId,
            AppointmentSegmentId = segment.Id.Value, Segment = segment, Status = ParticipationStatus.Confirmed
        };

        Booking valid = Booking(id => On(id, own));
        Assert.Equal(BookingStatusSummary.Confirmed, BookingSummary.StatusOf(valid));

        Assert.Throws<InvalidBookingParticipationStateException>(() => BookingParticipations.GetSingleParticipation(Booking()));
        // M0: more than one participation is no longer an integrity error but an AMBIGUOUS BookingId-addressed command.
        Assert.Equal(ErrorCodes.BookingParticipationAmbiguous, Assert.Throws<BusinessRuleException>(() => BookingParticipations.GetSingleParticipation(
            Booking(id => On(id, own), id => On(id, own)))).Code);
        Assert.Throws<InvalidBookingParticipationStateException>(() => BookingParticipations.GetSingleParticipation(
            Booking(id => On(id, foreignSegment))));
        Assert.Throws<InvalidBookingParticipationStateException>(() => BookingParticipations.GetSingleParticipation(
            Booking(id => On(id, own, Guid.NewGuid()))));
        Assert.Throws<InvalidBookingParticipationStateException>(() => BookingParticipations.GetSingleParticipation(
            Booking(_ => On(Guid.NewGuid(), own))));
        // The derived summary never invents a status for a Booking without participations.
        Assert.Throws<InvalidBookingParticipationStateException>(() => BookingSummary.StatusOf(Booking()));
    }

    #endregion

    #region Deletion rule: untouched = deletable

    [Fact]
    public async Task Update_RemovesANewlyCreatedConfirmedParticipation_AndItsBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Update_RemovesANewlyCreatedConfirmedParticipation_AndItsBooking));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);

        await UpdateOmitting(w, created.Id, w.Client);

        Assert.Equal((0, 0), await Rows(w, created.Id, second)); // removed, not cancelled
        Assert.Equal((1, 1), await Rows(w, created.Id, w.Client));
        AppointmentDto read = await w.Appointments.GetById(w.OrganizationId, created.Id);
        Assert.Equal(w.Client.Id, Assert.Single(read.Bookings).ClientId);
    }

    [Fact]
    public async Task CompleteExisting_RemovesANewlyCreatedConfirmedParticipation_AndItsBooking()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteExisting_RemovesANewlyCreatedConfirmedParticipation_AndItsBooking));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);

        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10)));

        Assert.Equal((0, 0), await Rows(w, created.Id, second));
        Assert.Equal(ParticipationStatus.Completed, (await ParticipationOf(w, created.Id, w.Client)).Status);
    }

    [Fact]
    public async Task Delete_SameDayAppointmentWithOnlyUntouchedParticipations_ExplicitlyRemovesParticipationsAndBookings()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Delete_SameDayAppointmentWithOnlyUntouchedParticipations_ExplicitlyRemovesParticipationsAndBookings));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);

        await w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id);

        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.Appointments.AnyAsync(a => a.Id == created.Id));
        Assert.False(await db.Bookings.AnyAsync(b => b.AppointmentId == created.Id));
        Assert.False(await db.AppointmentSegments.AnyAsync(s => s.AppointmentId == created.Id));
        Assert.False(await db.BookingSegmentParticipations.AnyAsync(p => p.OrganizationId == w.OrganizationId));
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.NoShow)]
    public async Task TerminalParticipation_IsNotDeletedByOmission_AndBlocksAppointmentDelete(BookingStatus terminal)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(TerminalParticipation_IsNotDeletedByOmission_AndBlocksAppointmentDelete)}-{terminal}");
        Client active = await w.AddClient("Active", "Client");
        Appointment seeded = await w.SeedAppointment(Z(10),
            bookings: new[] { (w.Client, terminal, 50m), (active, BookingStatus.Confirmed, 50m) });

        // Update: the omitted terminal booking is history and is preserved, as before D3B1.
        await UpdateOmitting(w, seeded.Id.Value, active);
        Assert.Equal((1, 1), await Rows(w, seeded.Id.Value, w.Client));
        Assert.Equal(BookingParticipations.ToParticipationStatus(terminal), (await ParticipationOf(w, seeded.Id.Value, w.Client)).Status);

        // Delete: any participation with history blocks the whole appointment.
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, seeded.Id.Value));
        Assert.Equal((1, 1), await Rows(w, seeded.Id.Value, w.Client));
        Assert.Equal((1, 1), await Rows(w, seeded.Id.Value, active));
    }

    public static IEnumerable<object[]> ConfirmedWithHistory() => new[]
    {
        new object[] { "arrived", (Action<BookingSegmentParticipation>)(p => { p.ArrivedAt = DateTimeOffset.UtcNow; p.ArrivedBy = Guid.NewGuid(); }) },
        new object[] { "version", (Action<BookingSegmentParticipation>)(p => p.StatusVersion = 1) },
        new object[] { "reason", (Action<BookingSegmentParticipation>)(p => p.CancellationReason = "left over") },
        new object[] { "late", (Action<BookingSegmentParticipation>)(p => p.IsLateCancellation = false) },
    };

    [Theory]
    [MemberData(nameof(ConfirmedWithHistory))]
    public async Task ConfirmedParticipationWithHistory_BlocksOmissionAndDelete(string kind, Action<BookingSegmentParticipation> history)
    {
        await using SchedulingWorld w = await SchedulingWorld.Create($"{nameof(ConfirmedParticipationWithHistory_BlocksOmissionAndDelete)}-{kind}");
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);
        await Mutate(w, created.Id, second, history); // still Confirmed

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => UpdateOmitting(w, created.Id, w.Client));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.CompleteExisting(created.Id, w.CompleteRequest(Z(10))));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        Assert.Equal((1, 1), await Rows(w, created.Id, second));
        Assert.Equal((1, 1), await Rows(w, created.Id, w.Client));
        Assert.Equal(ParticipationStatus.Confirmed, (await ParticipationOf(w, created.Id, second)).Status);
        Assert.Equal(AppointmentStatus.Scheduled, (await w.LoadAppointment(created.Id)).Status); // CompleteExisting rolled back
    }

    [Fact]
    public async Task ConfirmedCorrectedBackFromCompleted_BlocksOmissionAndDelete()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ConfirmedCorrectedBackFromCompleted_BlocksOmissionAndDelete));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);
        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), settlements: new[]
        {
            new AppointmentClientSettlement { ClientId = w.Client.Id.Value, PaymentMethod = PaymentMethod.Cash, IsPaid = true },
            new AppointmentClientSettlement { ClientId = second.Id.Value, PaymentMethod = PaymentMethod.Cash, IsPaid = true }
        }));
        await w.SetBookingStatus(created.Id, second, BookingStatus.Confirmed); // Confirmed again, but with history

        BookingSegmentParticipation corrected = await ParticipationOf(w, created.Id, second);
        Assert.Equal((ParticipationStatus.Confirmed, 2), (corrected.Status, corrected.StatusVersion));

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => UpdateOmitting(w, created.Id, w.Client));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));
        Assert.Equal((1, 1), await Rows(w, created.Id, second));
    }

    [Fact]
    public async Task OneHistoricalParticipation_BlocksDeletingTheWholeAppointment()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(OneHistoricalParticipation_BlocksDeletingTheWholeAppointment));
        Client second = await w.AddClient("Second", "Client");
        Client third = await w.AddClient("Third", "Client");
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: new[] { second, third });
        await w.SetBookingStatus(created.Id, third, BookingStatus.Cancelled, "cannot come");

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        await using DatabaseContext db = w.NewDb();
        Assert.True(await db.Appointments.AnyAsync(a => a.Id == created.Id));
        Assert.Equal(3, await db.Bookings.CountAsync(b => b.AppointmentId == created.Id));
        Assert.Equal(3, await db.BookingSegmentParticipations.CountAsync(p => p.Booking.AppointmentId == created.Id));
    }

    [Fact]
    public void UntouchedDefinition_IsExactlyTheLockedRule()
    {
        BookingSegmentParticipation Fresh() => new() { Status = ParticipationStatus.Confirmed };

        Assert.True(ParticipationHistory.IsUntouched(Fresh()));
        foreach (ParticipationStatus status in new[] { ParticipationStatus.Completed, ParticipationStatus.Cancelled, ParticipationStatus.NoShow })
        {
            BookingSegmentParticipation p = Fresh();
            p.Status = status;
            Assert.False(ParticipationHistory.IsUntouched(p));
        }

        foreach ((string _, Action<BookingSegmentParticipation> history) in ConfirmedWithHistory().Select(x => ((string)x[0], (Action<BookingSegmentParticipation>)x[1])))
        {
            BookingSegmentParticipation p = Fresh();
            history(p);
            Assert.False(ParticipationHistory.IsUntouched(p));
        }

        BookingSegmentParticipation arrivedByOnly = Fresh();
        arrivedByOnly.ArrivedBy = Guid.NewGuid();
        Assert.False(ParticipationHistory.IsUntouched(arrivedByOnly));
    }

    #endregion
}
