#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase D3B3A — package usage is a PackageConsumption ledger on the participation (Consumed -&gt; Reversed, never
/// deleted, at most one active per participation), written only through IPackageConsumptionLedgerService. Package
/// validity — for eligibility AND consumption — is the service-performance date (segment start) as a company-local date
/// (F-08 fixed). Package is not money: no Payment, no price change.
/// </summary>
public class PackageConsumptionLedgerTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset Z(int h, int mi = 0) => SchedulingWorld.Future(h, mi);

    private static async Task<List<PackageConsumption>> ConsumptionsOf(SchedulingWorld w, Guid appointmentId, Client client)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.PackageConsumptions.AsNoTracking()
            .Where(c => c.Participation.Booking.AppointmentId == appointmentId && c.Participation.Booking.ClientId == client.Id)
            .OrderBy(c => c.CreatedAt).ToListAsync();
    }

    private static async Task<int?> Remaining(SchedulingWorld w, ClientPackage package) =>
        (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries;

    /// <summary>Runs the ledger directly inside one transaction on the tracked booking (as the services do).</summary>
    private static async Task<T> InLedger<T>(SchedulingWorld w, Guid appointmentId, Client client,
        Func<IPackageConsumptionLedgerService, IUnitOfWork, Appointment, Booking, Task<T>> body)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        Appointment appointment = await uow.Context.Appointments
            .Include(a => a.Segments).ThenInclude(s => s.Employees)
            .Include(a => a.Bookings)
            .SingleAsync(a => a.Id == appointmentId);
        Booking booking = appointment.Bookings.Single(b => b.ClientId == client.Id);
        T result = await body(w.Resolve<IPackageConsumptionLedgerService>(), uow, appointment, booking);
        await uow.CommitAsync();
        return result;
    }

    #region Eligibility

    [Fact]
    public async Task AValidPackage_CoversItsService_WithOneConsumptionOnTheParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AValidPackage_CoversItsService_WithOneConsumptionOnTheParticipation));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);

        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));

        PackageConsumption c = Assert.Single(await ConsumptionsOf(w, dto.Id, w.Client));
        Assert.Equal((package.Id.Value, w.Service.Id.Value, 1, PackageConsumptionStatus.Consumed),
            (c.ClientPackageId, c.ServiceId, c.Units, c.Status));
        Assert.Equal(SchedulingWorld.Past(10), c.ServiceStartsAt);
        Assert.Equal(w.ActorUserId, c.CreatedBy);
        Assert.Null(c.ReversedAt);
        Assert.Equal(4, await Remaining(w, package));
        // Package is not money: no Payment, the price snapshot is untouched, the obligation is settled by the package.
        Assert.Empty(await w.LoadPayments(dto.Bookings.Single().Id));
        BookingDto b = Assert.Single(dto.Bookings);
        Assert.Equal((50m, 50m, false, 0m, true), (b.Amount, b.SuggestedAmount, b.IsAmountManuallyOverridden, b.OutstandingAmount, b.IsPaid));
        await using DatabaseContext db = w.NewDb();
        BookingSegmentParticipation p = await db.BookingSegmentParticipations.AsNoTracking().SingleAsync(x => x.BookingId == b.Id);
        Assert.Equal((50m, (decimal?)50m, (PriceSource?)PriceSource.Default), (p.Amount, p.BaseAmount, p.BaseAmountSource));
    }

    [Fact]
    public async Task WrongService_WrongClient_Exhausted_AndExpired_AreRejected_WithoutAnyConsumption()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WrongService_WrongClient_Exhausted_AndExpired_AreRejected_WithoutAnyConsumption));
        ServiceEntity other = await w.AddService(30, 20m, name: "Other");
        Client stranger = await w.AddClient("Stranger", "Client");
        ClientPackage wrongService = await w.AddClientPackage(w.Client, other, 5, LongValid);
        ClientPackage wrongClient = await w.AddClientPackage(stranger, w.Service, 5, LongValid);
        ClientPackage exhausted = await w.AddClientPackage(w.Client, w.Service, 0, LongValid);
        ClientPackage expired = await w.AddClientPackage(w.Client, w.Service, 5, SchedulingWorld.Past(0).AddDays(-1));

        foreach (ClientPackage package in new[] { wrongService, wrongClient, exhausted, expired })
            await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
                () => w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id)));

        Assert.Equal(0, await w.CountAppointments());
        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.PackageConsumptions.AnyAsync(c => c.OrganizationId == w.OrganizationId));
    }

    [Fact]
    public async Task Validity_IsTheServicePerformanceDate_NotTheClock_InBothDirections()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Validity_IsTheServicePerformanceDate_NotTheClock_InBothDirections));
        // Valid until 2021: covers a 2020 appointment although it is expired today (F-08: before D3B3A this failed at deduction).
        ClientPackage oldPackage = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));
        AppointmentDto past = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: oldPackage.Id));
        Assert.Equal(PackageConsumptionStatus.Consumed, Assert.Single(await ConsumptionsOf(w, past.Id, w.Client)).Status);

        // Valid today but expired by the (future) appointment date: rejected although "now" is fine.
        ClientPackage shortPackage = await w.AddClientPackage(w.Client, w.Service, 5, SchedulingWorld.FutureDay.AddDays(-1));
        AppointmentDto future = await w.CreateAppointment(Z(10));
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteExisting(future.Id, w.CompleteRequest(Z(10), clientPackageId: shortPackage.Id)));
        Assert.Equal(5, await Remaining(w, shortPackage));
    }

    [Fact]
    public async Task Validity_UsesTheCompanyLocalDate_NotTheUtcDate()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Validity_UsesTheCompanyLocalDate_NotTheUtcDate), "Europe/Zagreb");
        // Expiry instant 2020-03-02T23:30Z is already 2020-03-03 00:30 in Zagreb (+01:00): the package's last valid LOCAL
        // date is 3 March. A service at 10:00 local on 3 March (09:00Z — a later UTC date than the expiry's) is covered;
        // the UTC date of the expiry (2 March) is not the business validity date.
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, new DateTimeOffset(2020, 3, 2, 23, 30, 0, TimeSpan.Zero));
        DateTimeOffset onTheLastLocalDay = new(2020, 3, 3, 10, 0, 0, TimeSpan.FromHours(1));
        DateTimeOffset dayAfter = new(2020, 3, 4, 10, 0, 0, TimeSpan.FromHours(1));

        AppointmentDto covered = await w.CompleteNew(w.CompleteRequest(onTheLastLocalDay, clientPackageId: package.Id));
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.CompleteNew(w.CompleteRequest(dayAfter, clientPackageId: package.Id)));

        Assert.Single(await ConsumptionsOf(w, covered.Id, w.Client));
        Assert.Equal(4, await Remaining(w, package));
    }

    #endregion

    #region Consumption: once, idempotent, concurrent

    [Fact]
    public async Task RepeatedConsumption_OfTheSameParticipation_DeductsOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RepeatedConsumption_OfTheSameParticipation_DeductsOnce));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));

        (PackageConsumption first, PackageConsumption second) = await InLedger(w, created.Id, w.Client, async (ledger, uow, a, b) =>
        {
            BookingExecutionContext execution = ExecutionContextResolver.ForBooking(a, b);
            PackageConsumption one = await ledger.Consume(uow, w.OrganizationId, w.ActorUserId, b, execution, package.Id.Value, BookingStatus.Completed);
            PackageConsumption two = await ledger.Consume(uow, w.OrganizationId, w.ActorUserId, b, execution, package.Id.Value, BookingStatus.Completed);
            return (one, two);
        });

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await ConsumptionsOf(w, created.Id, w.Client));
        Assert.Equal(4, await Remaining(w, package));
    }

    [Fact]
    public async Task Database_AllowsAtMostOneActiveConsumptionPerParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Database_AllowsAtMostOneActiveConsumptionPerParticipation));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto dto = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(10), clientPackageId: package.Id));
        PackageConsumption existing = Assert.Single(await ConsumptionsOf(w, dto.Id, w.Client));

        await using DatabaseContext db = w.NewDb();
        db.PackageConsumptions.Add(new PackageConsumption
        {
            Id = Guid.NewGuid(), OrganizationId = w.OrganizationId, ClientPackageId = package.Id.Value,
            BookingSegmentParticipationId = existing.BookingSegmentParticipationId, ServiceId = w.Service.Id.Value, Units = 1,
            ServiceStartsAt = existing.ServiceStartsAt, Status = PackageConsumptionStatus.Consumed, CreatedAt = DateTimeOffset.UtcNow
        });

        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("ux_package_consumptions_active_participation", ex.InnerException?.Message);
    }

    [Fact]
    public async Task TwoConcurrentCompletions_ForTheLastEntry_ExactlyOneConsumes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TwoConcurrentCompletions_ForTheLastEntry_ExactlyOneConsumes));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 1, LongValid);
        AppointmentDto a = await w.CreateAppointment(Z(9));
        AppointmentDto b = await w.CreateAppointment(Z(14));

        // Two independent scopes = two connections/transactions. Both pass the pre-transaction eligibility check; the
        // ledger locks the ClientPackage row FOR UPDATE, so the loser sees the winner's decrement.
        using IServiceScope scopeA = SchedulingTestHost.CreateScope();
        using IServiceScope scopeB = SchedulingTestHost.CreateScope();
        Task<(bool Ok, string Code)> Attempt(IServiceScope scope, AppointmentDto appointment, DateTimeOffset startsAt) => Task.Run(async () =>
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IAppointmentService>().CompleteExisting(
                    w.OrganizationId, w.ActorUserId, true, appointment.Id, w.CompleteRequest(startsAt, clientPackageId: package.Id));
                return (true, (string)null);
            }
            catch (BusinessRuleException ex)
            {
                return (false, ex.Code);
            }
        });

        (bool Ok, string Code)[] results = await Task.WhenAll(Attempt(scopeA, a, Z(9)), Attempt(scopeB, b, Z(14)));

        Assert.Equal(1, results.Count(r => r.Ok));
        Assert.Contains(results.Single(r => !r.Ok).Code, new[] { ErrorCodes.PackageNotEligible, ErrorCodes.ConcurrencyConflict });
        Assert.Equal(0, await Remaining(w, package));
        await using DatabaseContext db = w.NewDb();
        Assert.Equal(1, await db.PackageConsumptions.CountAsync(c => c.ClientPackageId == package.Id));
    }

    [Fact]
    public async Task AnUnlimitedPackage_RecordsAZeroUnitConsumption_AndAPoolPackage_OneUnit()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AnUnlimitedPackage_RecordsAZeroUnitConsumption_AndAPoolPackage_OneUnit));
        ClientPackage unlimited = await w.AddClientPackage(w.Client, w.Service, entries: null, LongValid);
        AppointmentDto first = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(9), clientPackageId: unlimited.Id));
        Client second = await w.AddClient("Pool", "Client");
        ClientPackage pool = await w.AddClientPackage(second, w.Service, 3, LongValid, PackageEntryMode.SharedPool);
        AppointmentDto other = await w.CompleteNew(w.CompleteRequest(SchedulingWorld.Past(12), client: second, clientPackageId: pool.Id));

        Assert.Equal(0, Assert.Single(await ConsumptionsOf(w, first.Id, w.Client)).Units);
        Assert.Null(await Remaining(w, unlimited));
        Assert.Equal(1, Assert.Single(await ConsumptionsOf(w, other.Id, second)).Units);
        Assert.Equal(2, (await w.LoadClientPackage(pool.Id.Value)).RemainingSharedEntries);
    }

    #endregion

    #region Reversal: history kept, never twice, consume again

    [Fact]
    public async Task Correction_ReversesTheConsumption_KeepingTheRow_AndAReCompletionConsumesAgain()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_ReversesTheConsumption_KeepingTheRow_AndAReCompletionConsumesAgain));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), clientPackageId: package.Id));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed); // correction

        PackageConsumption reversed = Assert.Single(await ConsumptionsOf(w, created.Id, w.Client));
        Assert.Equal(PackageConsumptionStatus.Reversed, reversed.Status);
        Assert.Equal(PackageConsumptionReversalReason.CompletionCorrection, reversed.ReversalReason);
        Assert.Equal(w.ActorUserId, reversed.ReversedBy);
        Assert.NotNull(reversed.ReversedAt);
        Assert.Equal(5, await Remaining(w, package));

        // D3B3A: a re-completion with the package consumes AGAIN as a new row (before, the stale
        // PackageCoverageApplied flag silently skipped the deduction and left the booking unsettled).
        AppointmentDto again = await w.CompleteExisting(created.Id, w.CompleteRequest(Z(10), clientPackageId: package.Id));

        List<PackageConsumption> history = await ConsumptionsOf(w, created.Id, w.Client);
        Assert.Equal(new[] { PackageConsumptionStatus.Reversed, PackageConsumptionStatus.Consumed }, history.Select(c => c.Status));
        Assert.Equal(4, await Remaining(w, package));
        BookingDto b = Assert.Single(again.Bookings);
        Assert.True(b.PackageCoverageApplied);
        Assert.False(b.PackageCoverageReturned);
        Assert.True(b.IsPaid);
    }

    [Fact]
    public async Task GroupCheckIn_UnCheckIn_CheckInAgain_IsAnExplainableLedger()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupCheckIn_UnCheckIn_CheckInAgain_IsAnExplainableLedger));
        ServiceEntity groupService = await w.AddGroupService();
        ClientPackage package = await w.AddClientPackage(w.Client, groupService, 5, LongValid);
        GroupDto group = await w.CreateGroup(groupService, capacity: 3);
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        Guid id = occurrence.Id.Value;

        await w.SetBookingStatus(id, w.Client, BookingStatus.Completed, clientPackageId: package.Id);
        await w.SetBookingStatus(id, w.Client, BookingStatus.NoShow); // un-check-in as no-show: coverage returned
        await w.SetBookingStatus(id, w.Client, BookingStatus.Completed, clientPackageId: package.Id);

        List<PackageConsumption> history = await ConsumptionsOf(w, id, w.Client);
        Assert.Equal(2, history.Count);
        Assert.Equal((PackageConsumptionStatus.Reversed, PackageConsumptionReversalReason.NoShow), (history[0].Status, history[0].ReversalReason));
        Assert.Equal(PackageConsumptionStatus.Consumed, history[1].Status);
        Assert.Equal(4, await Remaining(w, package));
        Assert.Equal(AttendanceCoverageType.SessionPackage, (await w.LoadBooking(id, w.Client)).CoverageType);
    }

    [Fact]
    public async Task RepeatedReversal_RestoresTheEntryOnlyOnce()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(RepeatedReversal_RestoresTheEntryOnlyOnce));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.SeedCoverageApplied(await w.LoadBooking(created.Id, w.Client), package); // 5 -> 4, active consumption

        (bool first, bool second) = await InLedger(w, created.Id, w.Client, async (ledger, uow, a, b) =>
            (await ledger.ReverseActive(uow, w.OrganizationId, w.ActorUserId, b, PackageConsumptionReversalReason.Cancellation),
             await ledger.ReverseActive(uow, w.OrganizationId, w.ActorUserId, b, PackageConsumptionReversalReason.Cancellation)));
        bool third = await InLedger(w, created.Id, w.Client, (ledger, uow, a, b) =>
            ledger.ReverseActive(uow, w.OrganizationId, w.ActorUserId, b, PackageConsumptionReversalReason.Cancellation));

        Assert.Equal((true, false, false), (first, second, third));
        Assert.Equal(5, await Remaining(w, package));
        Assert.Equal(PackageConsumptionStatus.Reversed, Assert.Single(await ConsumptionsOf(w, created.Id, w.Client)).Status);
    }

    [Fact]
    public async Task AppointmentCancel_WithReturn_ReversesWithTheCancellationReason()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentCancel_WithReturn_ReversesWithTheCancellationReason));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));
        await w.SeedCoverageApplied(await w.LoadBooking(created.Id, w.Client), package);

        await w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentCancelRequest
        {
            CancellationReason = "closed", ReturnEntryForClientIds = new List<Guid> { w.Client.Id.Value }
        });

        PackageConsumption c = Assert.Single(await ConsumptionsOf(w, created.Id, w.Client));
        Assert.Equal((PackageConsumptionStatus.Reversed, PackageConsumptionReversalReason.Cancellation), (c.Status, c.ReversalReason));
        Assert.Equal(5, await Remaining(w, package));
    }

    #endregion

    #region Timing setting

    [Fact]
    public async Task Timing_DefaultsToOnCompletion_AndOnlyACompletionTriggerConsumes()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Timing_DefaultsToOnCompletion_AndOnlyACompletionTriggerConsumes));
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10));

        OrganizationSettingsDto settings = await w.Resolve<IOrganizationSettingsService>().GetSettings(w.OrganizationId);
        Assert.Equal(PackageConsumptionTiming.OnCompletion, settings.PackageConsumptionTiming);

        PackageConsumption none = await InLedger(w, created.Id, w.Client, (ledger, uow, a, b) =>
            ledger.Consume(uow, w.OrganizationId, w.ActorUserId, b, ExecutionContextResolver.ForBooking(a, b), package.Id.Value, BookingStatus.Confirmed));

        Assert.Null(none);
        Assert.Empty(await ConsumptionsOf(w, created.Id, w.Client));
        Assert.Equal(5, await Remaining(w, package));
    }

    #endregion

    #region History rule

    [Fact]
    public async Task AParticipationWithPackageHistory_IsNotUntouched_EvenWhenItsLifecycleLooksUntouched()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AParticipationWithPackageHistory_IsNotUntouched_EvenWhenItsLifecycleLooksUntouched));
        Client second = await w.AddClient("Second", "Client");
        ClientPackage package = await w.AddClientPackage(second, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(Z(10), extraClients: second);
        await w.SeedCoverageApplied(await w.LoadBooking(created.Id, second), package); // Confirmed, StatusVersion 0, consumed

        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete, () => w.Appointments.Update(
            w.OrganizationId, w.ActorUserId, true, created.Id, w.UpdateRequest(created, r => r.ClientIds = new List<Guid> { w.Client.Id.Value })));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        // A REVERSED consumption is history too.
        await InLedger(w, created.Id, second, (ledger, uow, a, b) =>
            ledger.ReverseActive(uow, w.OrganizationId, w.ActorUserId, b, PackageConsumptionReversalReason.Cancellation));
        Booking b = await w.LoadBooking(created.Id, second);
        Assert.Equal((BookingStatus.Confirmed, 0), (b.Status, b.StatusVersion));
        await SchedulingAssert.BusinessRule(ErrorCodes.ReferencedCannotDelete,
            () => w.Appointments.Delete(w.OrganizationId, w.ActorUserId, created.Id));

        Assert.Equal(2, (await w.LoadAppointment(created.Id)).Bookings.Count);
        Assert.Single(await ConsumptionsOf(w, created.Id, second));
    }

    #endregion

    #region Schema

    [Fact]
    public async Task Schema_LedgerConstraintsAndIndexesExist_AndBookingHasNoPackageColumns()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Schema_LedgerConstraintsAndIndexesExist_AndBookingHasNoPackageColumns));
        await using DatabaseContext db = w.NewDb();

        List<string> constraints = await db.Database.SqlQueryRaw<string>(@"
            SELECT conname AS ""Value"" FROM pg_constraint WHERE conrelid = 'dunelight.package_consumptions'::regclass ORDER BY conname").ToListAsync();
        Assert.Equal(new[]
        {
            "ck_package_consumptions_reversal", "ck_package_consumptions_reversal_reason", "ck_package_consumptions_status",
            "ck_package_consumptions_units", "fk_package_consumptions_client_package_id", "fk_package_consumptions_organization_id",
            "fk_package_consumptions_participation_id", "fk_package_consumptions_service_id", "pk_package_consumptions"
        }, constraints);
        // Every FK is RESTRICT/NO ACTION: consumption history is never cascaded away.
        Assert.Empty(await db.Database.SqlQueryRaw<string>(@"
            SELECT conname AS ""Value"" FROM pg_constraint
             WHERE conrelid = 'dunelight.package_consumptions'::regclass AND contype = 'f' AND confdeltype NOT IN ('a', 'r')").ToListAsync());

        List<string> indexes = await db.Database.SqlQueryRaw<string>(@"
            SELECT indexdef AS ""Value"" FROM pg_indexes WHERE schemaname = 'dunelight' AND tablename = 'package_consumptions'").ToListAsync();
        Assert.Contains(indexes, i => i.Contains("ux_package_consumptions_active_participation") && i.Contains("UNIQUE") && i.Contains("'Consumed'"));
        Assert.Contains(indexes, i => i.Contains("ix_package_consumptions_client_package_id"));
        Assert.Contains(indexes, i => i.Contains("ix_package_consumptions_participation_id"));

        Assert.Empty(await db.Database.SqlQueryRaw<string>(@"
            SELECT column_name AS ""Value"" FROM information_schema.columns WHERE table_schema = 'dunelight' AND table_name = 'bookings'
               AND (column_name LIKE '%package%' OR column_name = 'coverage_type')").ToListAsync());
        Assert.Single(await db.Database.SqlQueryRaw<string>(@"
            SELECT column_name AS ""Value"" FROM information_schema.columns WHERE table_schema = 'dunelight'
               AND table_name = 'organization_settings' AND column_name = 'package_consumption_timing'").ToListAsync());
    }

    [Fact]
    public void UntouchedDefinition_CountsAnyPackageConsumption()
    {
        BookingSegmentParticipation fresh = new() { Status = ParticipationStatus.Confirmed };
        Assert.True(ParticipationHistory.IsUntouched(fresh));

        fresh.PackageConsumptions.Add(new PackageConsumption { Status = PackageConsumptionStatus.Reversed });
        Assert.False(ParticipationHistory.IsUntouched(fresh));
    }

    #endregion
}
