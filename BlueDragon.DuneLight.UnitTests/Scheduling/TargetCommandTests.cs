using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Groups;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Phase M1H — the narrow target commands that replace the removed flat compatibility surface:
/// <list type="bullet">
/// <item>CompleteNow (POST /api/appointments/complete): the atomic POS "record work already done" command — ONE explicit
/// segment with the full M1G multi-employee/pricing rules, one Booking + one Participation per client, every participation
/// Completed through the participation lifecycle core, everything in one transaction;</item>
/// <item>ChangeNote (PATCH /api/appointments/{id}/note): the appointment note only;</item>
/// <item>SetParticipationPrice (PATCH /api/participations/{id}/price): the manual final price of ONE participation.</item>
/// </list>
/// Appointment company reassignment is intentionally not exposed by any target command (pinned by reflection below).
/// </summary>
public class TargetCommandTests
{
    private static readonly DateTimeOffset LongValid = new(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>"Duo" (default 50, Company+Service 55, Ana 60, Marko 70) and three employees eligible for every service.</summary>
    private sealed record Studio(ServiceEntity Duo, Employee Ana, Employee Marko, Employee Ivana);

    private static async Task<Studio> SetUp(SchedulingWorld w)
    {
        ServiceEntity duo = await w.AddService(60, 50m, name: "Duo");
        Employee ana = await w.AddEmployee("Ana", assignedToService: false);
        Employee marko = await w.AddEmployee("Marko", assignedToService: false);
        Employee ivana = await w.AddEmployee("Ivana", assignedToService: false);
        await w.AddPriceListItem(duo, 55m, SchedulingWorld.Day(SchedulingWorld.PastDay.AddYears(-1)), companyId: w.Company.Id);
        await w.AddPriceListItem(duo, 60m, SchedulingWorld.Day(SchedulingWorld.PastDay.AddYears(-1)), employeeId: ana.Id);
        await w.AddPriceListItem(duo, 70m, SchedulingWorld.Day(SchedulingWorld.PastDay.AddYears(-1)), companyId: w.Company.Id, employeeId: marko.Id);
        return new Studio(duo, ana, marko, ivana);
    }

    private static AppointmentCompleteNowRequest CompleteNow(
        SchedulingWorld w, ServiceEntity service, DateTimeOffset start, Employee[] employees, SegmentPricingMode? mode = null,
        Employee pricing = null, Room room = null, List<AppointmentSegmentResourceRequest> resources = null,
        params AppointmentCompletedClientRequest[] clients) => new()
    {
        CompanyId = w.Company.Id.Value,
        Segment = new AppointmentSegmentDefinitionRequest
        {
            ServiceId = service.Id.Value,
            PlannedStart = start,
            EmployeeIds = employees.Select(e => e.Id.Value).ToList(),
            PricingMode = mode,
            PricingEmployeeId = pricing?.Id,
            RoomId = room?.Id,
            Resources = resources ?? new List<AppointmentSegmentResourceRequest>()
        },
        Clients = clients.ToList()
    };

    private static AppointmentCompletedClientRequest Paid(Client client, PaymentMethod method = PaymentMethod.Cash, decimal? amount = null) =>
        new() { ClientId = client.Id.Value, PaymentMethod = method, Amount = amount };

    private static Task<AppointmentDto> Run(SchedulingWorld w, AppointmentCompleteNowRequest request, bool fullScope = true, Guid? userId = null) =>
        w.Appointments.CompleteNow(w.OrganizationId, userId ?? w.ActorUserId, fullScope, request);

    private static async Task<(int Appointments, int Segments, int Bookings, int Participations, int Payments, int Commissions)> Footprint(SchedulingWorld w)
    {
        await using DatabaseContext db = w.NewDb();
        return (await db.Appointments.CountAsync(a => a.OrganizationId == w.OrganizationId),
            await db.AppointmentSegments.CountAsync(s => s.OrganizationId == w.OrganizationId),
            await db.Bookings.CountAsync(b => b.OrganizationId == w.OrganizationId),
            await db.BookingSegmentParticipations.CountAsync(p => p.OrganizationId == w.OrganizationId),
            await db.Payments.CountAsync(p => p.OrganizationId == w.OrganizationId),
            await db.CommissionEntries.CountAsync(c => c.OrganizationId == w.OrganizationId));
    }

    private static async Task<List<BookingSegmentParticipation>> ParticipationsOf(SchedulingWorld w, Guid appointmentId)
    {
        await using DatabaseContext db = w.NewDb();
        return await db.BookingSegmentParticipations.AsNoTracking().IgnoreAutoIncludes().Include(p => p.Booking)
            .Where(p => p.Booking.AppointmentId == appointmentId).ToListAsync();
    }

    #region CompleteNow — staffing and pricing source (M1G rules)

    [Fact]
    public async Task CompleteNow_OneEmployee_OneSegment_ParticipationCompletedThroughTheLifecycle()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_OneEmployee_OneSegment_ParticipationCompletedThroughTheLifecycle));
        Studio s = await SetUp(w);

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana }, clients: Paid(w.Client)));

        AppointmentSegmentDto segment = Assert.Single(dto.Segments);
        Assert.Equal((SegmentPricingMode.Employee, s.Ana.Id), (segment.PricingMode, segment.PricingEmployeeId)); // automatic
        Assert.Equal(AppointmentStatus.Closed, dto.Status);
        BookingSegmentParticipation p = Assert.Single(await ParticipationsOf(w, dto.Id));
        Assert.Equal((ParticipationStatus.Completed, 1, 60m), (p.Status, p.StatusVersion, p.Amount)); // Ana's tier
        Assert.Single(await w.LoadAuditLog(dto.Id), l => l.ChangeType == "BookingStatus" && l.BookingSegmentParticipationId == p.Id);
        Assert.Equal(60m, Assert.Single(await w.LoadPayments(p.BookingId)).Amount);
    }

    [Fact]
    public async Task CompleteNow_TwoEmployees_WithEmployeePricing_UsesTheSelectedEmployeesTier()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_TwoEmployees_WithEmployeePricing_UsesTheSelectedEmployeesTier));
        Studio s = await SetUp(w);

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana, s.Marko },
            SegmentPricingMode.Employee, s.Marko, clients: Paid(w.Client)));

        AppointmentSegmentDto segment = Assert.Single(dto.Segments);
        Assert.Equal(new[] { s.Ana.Id.Value, s.Marko.Id.Value }.OrderBy(x => x), segment.Employees.Select(e => e.EmployeeId).OrderBy(x => x));
        Assert.Equal((SegmentPricingMode.Employee, s.Marko.Id), (segment.PricingMode, segment.PricingEmployeeId));
        BookingSegmentParticipation p = Assert.Single(await ParticipationsOf(w, dto.Id));
        Assert.Equal((70m, 70m, PriceSource.EmployeeCompanySpecific), (p.Amount, p.SuggestedAmount, p.BaseAmountSource));
    }

    [Fact]
    public async Task CompleteNow_TwoEmployees_WithStandardPricing_SkipsEveryEmployeeTier()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_TwoEmployees_WithStandardPricing_SkipsEveryEmployeeTier));
        Studio s = await SetUp(w);

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana, s.Marko },
            SegmentPricingMode.Standard, clients: Paid(w.Client)));

        Assert.Equal((SegmentPricingMode.Standard, (Guid?)null), (dto.Segments[0].PricingMode, dto.Segments[0].PricingEmployeeId));
        Assert.Equal(55m, Assert.Single(await ParticipationsOf(w, dto.Id)).Amount); // Company+Service, no employee tier
    }

    [Fact]
    public async Task CompleteNow_AnInvalidPricingSource_IsRejected_AndNothingIsWritten()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_AnInvalidPricingSource_IsRejected_AndNothingIsWritten));
        Studio s = await SetUp(w);

        // Two employees without a choice: the source is required (never inferred).
        Assert.Equal(ErrorCodes.PricingSourceRequired, (await SchedulingAssert.Validation(
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana, s.Marko }, clients: Paid(w.Client))))).Code);
        // Employee pricing by someone who does not work the segment, and Standard for a single employee.
        Assert.Equal(ErrorCodes.InvalidPricingSource, (await SchedulingAssert.Validation(
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana, s.Marko }, SegmentPricingMode.Employee, s.Ivana, clients: Paid(w.Client))))).Code);
        Assert.Equal(ErrorCodes.InvalidPricingSource, (await SchedulingAssert.Validation(
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana }, SegmentPricingMode.Standard, clients: Paid(w.Client))))).Code);

        Assert.Equal((0, 0, 0, 0, 0, 0), await Footprint(w));
    }

    [Fact]
    public async Task CompleteNow_CommissionIsEarnedPerEmployee_FromTheParticipationsFinalPrice()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_CommissionIsEarnedPerEmployee_FromTheParticipationsFinalPrice));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Duo, CommissionCalculationType.Percentage, 10m);
        await w.AddCommissionRule(s.Marko, s.Duo, CommissionCalculationType.Fixed, 5m);

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana, s.Marko },
            SegmentPricingMode.Standard, clients: Paid(w.Client, amount: 40m)));

        BookingSegmentParticipation p = Assert.Single(await ParticipationsOf(w, dto.Id));
        List<CommissionEntry> entries = await w.LoadCommissionEntries();
        Assert.Equal(new[] { (s.Ana.Id.Value, 4m), (s.Marko.Id.Value, 5m) }.OrderBy(x => x.Item1),
            entries.Select(e => (e.EmployeeId, e.CommissionAmount)).OrderBy(x => x.Item1));
        Assert.All(entries, e =>
        {
            Assert.Equal(p.Id, e.BookingSegmentParticipationId);
            Assert.Equal(40m, e.BaseAmount); // the manual final price
            Assert.Equal(1, e.SourceVersion);
        });
    }

    #endregion

    #region CompleteNow — clients, settlement and atomicity

    [Fact]
    public async Task CompleteNow_SeveralClients_OneBookingAndOneIndependentlyPricedParticipationEach()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_SeveralClients_OneBookingAndOneIndependentlyPricedParticipationEach));
        Studio s = await SetUp(w);
        Client second = await w.AddClient("Second", "Client");
        Client third = await w.AddClient("Third", "Client");

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana },
            clients: new[] { Paid(w.Client), Paid(second, amount: 30m), new AppointmentCompletedClientRequest { ClientId = third.Id.Value } }));

        Assert.Equal(3, dto.Bookings.Count);
        List<BookingSegmentParticipation> all = await ParticipationsOf(w, dto.Id);
        Assert.Equal(3, all.Count);
        Assert.All(all, p => Assert.Equal((ParticipationStatus.Completed, 1, dto.Segments[0].Id), (p.Status, p.StatusVersion, p.AppointmentSegmentId)));
        BookingSegmentParticipation Of(Client c) => all.Single(p => p.Booking.ClientId == c.Id);
        Assert.Equal((60m, false), (Of(w.Client).Amount, Of(w.Client).IsAmountManuallyOverridden));
        Assert.Equal((30m, 60m, true), (Of(second).Amount, Of(second).SuggestedAmount, Of(second).IsAmountManuallyOverridden));
        Assert.Empty(await w.LoadPayments(Of(third).BookingId)); // no payment method: recorded, settled later
        Assert.False(dto.Bookings.Single(b => b.ClientId == third.Id).IsPaid);
    }

    [Fact]
    public async Task CompleteNow_OnePackageCoveredClient_AndOneMonetaryClient()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_OnePackageCoveredClient_AndOneMonetaryClient));
        Studio s = await SetUp(w);
        Client payer = await w.AddClient("Payer", "Client");
        ClientPackage package = await w.AddClientPackage(w.Client, s.Duo, 5, LongValid);

        AppointmentDto dto = await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana }, clients: new[]
        {
            new AppointmentCompletedClientRequest { ClientId = w.Client.Id.Value, ClientPackageId = package.Id, PaymentMethod = PaymentMethod.Cash },
            Paid(payer, PaymentMethod.Card)
        }));

        Booking covered = await w.LoadBooking(dto.Id, w.Client);
        Booking paid = await w.LoadBooking(dto.Id, payer);
        Assert.True(covered.PackageCoverageApplied);
        Assert.Empty(await w.LoadPayments(covered.Id.Value)); // the package settles it — the payment method is ignored
        Assert.Equal(4, (await w.LoadClientPackage(package.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.False(paid.PackageCoverageApplied);
        Assert.Equal((PaymentMethod.Card, 60m), (Assert.Single(await w.LoadPayments(paid.Id.Value)).Method, (await w.LoadPayments(paid.Id.Value)).Single().Amount));
        Assert.All(dto.Bookings, b => Assert.True(b.IsPaid));
    }

    [Fact]
    public async Task CompleteNow_AFailingSettlementMidway_RollsBackEverything()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_AFailingSettlementMidway_RollsBackEverything));
        Studio s = await SetUp(w);
        await w.AddCommissionRule(s.Ana, s.Duo, CommissionCalculationType.Fixed, 5m);
        Client other = await w.AddClient("Other", "Client");
        // Clients are completed in ClientId order: the LAST one fails, after the first one was completed and paid.
        Client[] ordered = new[] { w.Client, other }.OrderBy(c => c.Id.Value).ToArray();
        ServiceEntity unrelated = await w.AddService(30, 20m, name: "Unrelated");
        ClientPackage wrongService = await w.AddClientPackage(ordered[1], unrelated, 5, LongValid);
        ClientPackage untouched = await w.AddClientPackage(ordered[0], s.Duo, 5, LongValid);

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible, () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana },
            clients: new[]
            {
                new AppointmentCompletedClientRequest { ClientId = ordered[0].Id.Value, ClientPackageId = untouched.Id },
                new AppointmentCompletedClientRequest { ClientId = ordered[1].Id.Value, ClientPackageId = wrongService.Id }
            })));
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible, () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(11), new[] { s.Ana },
            clients: new[]
            {
                Paid(ordered[0], PaymentMethod.Card),
                new AppointmentCompletedClientRequest { ClientId = ordered[1].Id.Value, ClientPackageId = wrongService.Id }
            })));

        // No Appointment / Segment / Booking / Participation / Payment / Commission survives, and no package entry is used.
        Assert.Equal((0, 0, 0, 0, 0, 0), await Footprint(w));
        Assert.Equal(5, (await w.LoadClientPackage(untouched.Id.Value)).ServiceEntries.Single().RemainingEntries);
        await using DatabaseContext db = w.NewDb();
        Assert.False(await db.PackageConsumptions.AnyAsync(c => c.OrganizationId == w.OrganizationId));
        Assert.False(await db.Checkouts.AnyAsync(c => c.OrganizationId == w.OrganizationId));
    }

    [Fact]
    public async Task CompleteNow_ValidatesTheRequestShape()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_ValidatesTheRequestShape));
        Studio s = await SetUp(w);

        await SchedulingAssert.Validation(() => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana })));       // no client
        await SchedulingAssert.Validation(() => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana },
            clients: new[] { Paid(w.Client), Paid(w.Client, PaymentMethod.Card) })));                                                 // same client twice
        AppointmentCompleteNowRequest noSegment = CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana }, clients: Paid(w.Client));
        noSegment.Segment = null;
        await SchedulingAssert.Validation(() => Run(w, noSegment));

        Assert.Equal((0, 0, 0, 0, 0, 0), await Footprint(w));
    }

    #endregion

    #region CompleteNow — hard scheduling rules and time

    [Fact]
    public async Task CompleteNow_RespectsRoomPeopleAndResourceCapacity()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_RespectsRoomPeopleAndResourceCapacity));
        Studio s = await SetUp(w);
        Client second = await w.AddClient("Second", "Client");
        Room room = await w.AddRoom(capacity: 2);
        Resource table = await w.AddResource(capacity: 1, name: "Table");

        // 1 employee + 2 clients = 3 people in a 2-person room.
        await SchedulingAssert.BusinessRule(ErrorCodes.RoomCapacityExceeded, () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana },
            room: room, clients: new[] { Paid(w.Client), Paid(second) })));

        await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(12), new[] { s.Ana },
            resources: new List<AppointmentSegmentResourceRequest> { new() { ResourceId = table.Id.Value } }, clients: Paid(w.Client)));
        await SchedulingAssert.BusinessRule(ErrorCodes.ResourceCapacityExceeded, () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(12, 30), new[] { s.Marko },
            resources: new List<AppointmentSegmentResourceRequest> { new() { ResourceId = table.Id.Value } }, clients: Paid(second))));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task CompleteNow_RespectsEmployeeAndClientOverlap()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_RespectsEmployeeAndClientOverlap));
        Studio s = await SetUp(w);
        Client other = await w.AddClient("Other", "Client");
        await Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10), new[] { s.Ana }, clients: Paid(w.Client)));

        // The same employee (with another client) and the same client (with another employee) are both busy.
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10, 30), new[] { s.Ana, s.Marko }, SegmentPricingMode.Standard, clients: Paid(other))));
        await SchedulingAssert.BusinessRule(ErrorCodes.AppointmentOverlap,
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(10, 30), new[] { s.Marko }, clients: Paid(w.Client))));

        Assert.Equal(1, await w.CountAppointments());
    }

    [Fact]
    public async Task CompleteNow_APastPerformedService_IsValidatedLikeTheFuture_AndRecordedWithOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(CompleteNow_APastPerformedService_IsValidatedLikeTheFuture_AndRecordedWithOverride));
        Studio s = await SetUp(w);
        await w.AddAbsence(s.Ana, SchedulingWorld.PastDay);

        // CHANGED in K1 (K1-1): the past is validated against the workforce like the future — absent / outside hours is refused
        // without override, even for a user with appointments.write.all.
        await SchedulingAssert.BusinessRule(ErrorCodes.EmployeeAbsent,
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(22), new[] { s.Ana }, clients: Paid(w.Client))));
        await SchedulingAssert.BusinessRule(ErrorCodes.OutsideWorkingHours,
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(22), new[] { s.Marko }, clients: Paid(w.Client))));

        // Override (appointments.write.all) records it with a warning.
        AppointmentCompleteNowRequest overridden = CompleteNow(w, s.Duo, SchedulingWorld.Past(22), new[] { s.Ana }, clients: Paid(w.Client));
        overridden.OverrideAvailability = true;
        AppointmentDto dto = await Run(w, overridden);
        Assert.Equal(AppointmentStatus.Closed, dto.Status);
        Assert.Contains(dto.Warnings, x => x.Code == WarningCodes.EmployeeAbsent);

        // ... but the structural rules still apply (an inactive client is refused).
        Client inactive = await w.AddClient("Inactive", "Client", isActive: false);
        await SchedulingAssert.BusinessRule(ErrorCodes.InactiveClient,
            () => Run(w, CompleteNow(w, s.Duo, SchedulingWorld.Past(9), new[] { s.Marko }, clients: Paid(inactive))));
    }

    #endregion

    #region ChangeNote

    [Fact]
    public async Task ChangeNote_ChangesOnlyTheNote()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeNote_ChangesOnlyTheNote));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(w.CreateRequest(SchedulingWorld.Future(10), note: "old", extraClients: second));
        await w.SetParticipationPrice(created.Id, second, 35m);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "late");
        AppointmentDto before = await w.Appointments.GetById(w.OrganizationId, created.Id);
        int auditBefore = (await w.LoadAuditLog(created.Id)).Count;

        AppointmentDto after = await w.Appointments.ChangeNote(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentNoteChangeRequest { Note = "new note" });

        Assert.Equal("new note", after.Note);
        Assert.Equal("new note", (await w.LoadAppointment(created.Id)).Note);
        // Segments, pricing, schedule and lifecycle are untouched.
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CompanyId, after.CompanyId);
        Assert.Equal((before.PlannedStart, before.PlannedEnd), (after.PlannedStart, after.PlannedEnd));
        AppointmentSegmentDto segmentBefore = Assert.Single(before.Segments), segmentAfter = Assert.Single(after.Segments);
        Assert.Equal((segmentBefore.Id, segmentBefore.ServiceId, segmentBefore.PlannedStart, segmentBefore.PlannedEnd, segmentBefore.RoomId, segmentBefore.PricingMode),
            (segmentAfter.Id, segmentAfter.ServiceId, segmentAfter.PlannedStart, segmentAfter.PlannedEnd, segmentAfter.RoomId, segmentAfter.PricingMode));
        Assert.Equal(segmentBefore.Employees.Select(e => e.EmployeeId), segmentAfter.Employees.Select(e => e.EmployeeId));
        Assert.Equal(
            before.Bookings.SelectMany(b => b.Participations).Select(p => (p.Id, p.Status, p.Amount)).OrderBy(x => x.Id),
            after.Bookings.SelectMany(b => b.Participations).Select(p => (p.Id, p.Status, p.Amount)).OrderBy(x => x.Id));
        Assert.Equal(auditBefore, (await w.LoadAuditLog(created.Id)).Count);

        // Clearing is a note change too.
        Assert.Null((await w.Appointments.ChangeNote(w.OrganizationId, w.ActorUserId, true, created.Id, new AppointmentNoteChangeRequest())).Note);
    }

    [Fact]
    public async Task ChangeNote_KeepsTheExistingOwnershipRule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ChangeNote_KeepsTheExistingOwnershipRule));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // CHANGED in T1: 403 OutOfScope (bilo 409 NOT_OWNER)
        await SchedulingAssert.OutOfScope(() => w.Appointments.ChangeNote(w.OrganizationId, other.UserId, false, created.Id,
            new AppointmentNoteChangeRequest { Note = "not mine" }));
        AppointmentDto own = await w.Appointments.ChangeNote(w.OrganizationId, w.Employee.UserId, false, created.Id,
            new AppointmentNoteChangeRequest { Note = "mine" });

        Assert.Equal("mine", own.Note);
        await SchedulingAssert.NotFound(() => w.Appointments.ChangeNote(w.OrganizationId, w.ActorUserId, true, Guid.NewGuid(), new AppointmentNoteChangeRequest()));
    }

    #endregion

    #region SetParticipationPrice

    [Fact]
    public async Task ParticipationPrice_ChangesTheManualPriceOfThatParticipationOnly_KeepingTheResolutionSnapshot()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ParticipationPrice_ChangesTheManualPriceOfThatParticipationOnly_KeepingTheResolutionSnapshot));
        Client second = await w.AddClient("Second", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10), extraClients: second);
        Guid participationId = await w.ParticipationIdOnOnlySegment(created.Id, second.Id.Value);
        BookingSegmentParticipation before = (await ParticipationsOf(w, created.Id)).Single(p => p.Id == participationId);

        BookingDto dto = await w.Bookings.SetParticipationPrice(w.OrganizationId, w.ActorUserId, true, participationId,
            new ParticipationPriceChangeRequest { Amount = 30m });

        Assert.Equal((30m, 50m, true), (dto.Amount, dto.SuggestedAmount, dto.IsAmountManuallyOverridden));
        List<BookingSegmentParticipation> all = await ParticipationsOf(w, created.Id);
        BookingSegmentParticipation p = all.Single(x => x.Id == participationId);
        Assert.Equal((30m, true), (p.Amount, p.IsAmountManuallyOverridden));
        Assert.Equal((before.SuggestedAmount, before.BaseAmount, before.BaseAmountSource, before.PricingMode, before.PricingEmployeeId),
            (p.SuggestedAmount, p.BaseAmount, p.BaseAmountSource, p.PricingMode, p.PricingEmployeeId));
        Assert.Equal((ParticipationStatus.Confirmed, 0), (p.Status, p.StatusVersion)); // pricing is not lifecycle history
        BookingSegmentParticipation sibling = all.Single(x => x.Id != participationId);
        Assert.Equal((50m, false), (sibling.Amount, sibling.IsAmountManuallyOverridden));
        AppointmentAuditLog audit = Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "Amount");
        Assert.Equal((participationId, 50m, 30m), (audit.BookingSegmentParticipationId.Value,
            decimal.Parse(audit.OldValue, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(audit.NewValue, System.Globalization.CultureInfo.InvariantCulture)));

        // Null = back to the suggested price (no manual marker).
        BookingDto reset = await w.Bookings.SetParticipationPrice(w.OrganizationId, w.ActorUserId, true, participationId, new ParticipationPriceChangeRequest());
        Assert.Equal((50m, false), (reset.Amount, reset.IsAmountManuallyOverridden));
    }

    [Fact]
    public async Task ParticipationPrice_SettlementFollowsTheNewPrice_AndAPriceBelowWhatWasPaidIsRejected()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ParticipationPrice_SettlementFollowsTheNewPrice_AndAPriceBelowWhatWasPaidIsRejected));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 20m);

        BookingDto repriced = await w.SetParticipationPrice(created.Id, w.Client, 30m);
        Assert.Equal((30m, 20m, 10m), (repriced.Amount, repriced.PaidAmount, repriced.OutstandingAmount));

        await SchedulingAssert.BusinessRule(ErrorCodes.PaymentExceedsOutstandingAmount, () => w.SetParticipationPrice(created.Id, w.Client, 15m));
        Assert.Equal(30m, (await w.LoadBooking(created.Id, w.Client)).Amount);

        // Package/payment exclusivity is unchanged: completing the paid participation with a package is still refused.
        ClientPackage package = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await SchedulingAssert.BusinessRule(ErrorCodes.BookingAlreadyHasMonetaryPayment,
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Completed, clientPackageId: package.Id));
    }

    [Fact]
    public async Task ParticipationPrice_IsRefusedForHistoricalStates_AndForAGroupParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ParticipationPrice_IsRefusedForHistoricalStates_AndForAGroupParticipation));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll); // P1: Business cancellation needs appointments.write.all
        Client cancelled = await w.AddClient("Cancelled", "Client");
        Client noShow = await w.AddClient("NoShow", "Client");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10), extraClients: new[] { cancelled, noShow });
        await w.CompleteParticipations(created.Id, w.CompleteRequest(SchedulingWorld.Past(10), paymentMethod: PaymentMethod.Cash));
        await w.SetBookingStatus(created.Id, cancelled, BookingStatus.Cancelled, "x", initiator: CancellationInitiator.Business); // P1: after start only Business may cancel
        await w.SetBookingStatus(created.Id, noShow, BookingStatus.NoShow);

        foreach (Client client in new[] { w.Client, cancelled, noShow })
            await SchedulingAssert.BusinessRule(ErrorCodes.AlreadyCompleted, () => w.SetParticipationPrice(created.Id, client, 10m));
        Assert.Equal(50m, (await w.LoadBooking(created.Id, w.Client)).Amount);

        ServiceEntity groupService = await w.AddGroupService();
        GroupDto group = await w.CreateGroup(groupService, capacity: 5, slots: (SchedulingWorld.FutureDay.DayOfWeek, TimeSpan.FromHours(16)));
        await w.AddGroupMember(group, w.Client);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await SchedulingAssert.Validation(() => w.SetParticipationPrice(occurrence.Id.Value, w.Client, 10m));
        await SchedulingAssert.NotFound(() => w.Bookings.SetParticipationPrice(w.OrganizationId, w.ActorUserId, true, Guid.NewGuid(), new ParticipationPriceChangeRequest()));
    }

    [Fact]
    public async Task ParticipationPrice_KeepsTheExistingOwnershipRule()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ParticipationPrice_KeepsTheExistingOwnershipRule));
        Employee other = await w.AddEmployee("Other");
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        // CHANGED in T1: 403 OutOfScope (bilo 409 NOT_OWNER)
        await SchedulingAssert.OutOfScope(() => w.SetParticipationPrice(created.Id, w.Client, 10m, hasFullScope: false, userId: other.UserId));
        BookingDto own = await w.SetParticipationPrice(created.Id, w.Client, 10m, hasFullScope: false, userId: w.Employee.UserId);

        Assert.Equal(10m, own.Amount);
    }

    #endregion

    #region Company reassignment is not a target capability

    [Fact]
    public void NoTargetCommandOnAnExistingAppointment_CarriesACompanyId()
    {
        // Every command addressing an EXISTING appointment / segment / participation / booking takes its id plus a request;
        // none of those requests may carry a CompanyId (company reassignment is intentionally not exposed).
        string[] addressing = { "id", "appointmentId", "segmentId", "participationId" };
        List<string> offenders = new[] { typeof(IAppointmentService), typeof(IBookingService) }
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(Guid) && addressing.Contains(p.Name)))
            .SelectMany(m => m.GetParameters().Where(p => p.ParameterType.IsClass && p.ParameterType != typeof(string))
                .Where(p => p.ParameterType.GetProperty("CompanyId", BindingFlags.Public | BindingFlags.Instance) != null)
                .Select(p => $"{m.DeclaringType.Name}.{m.Name}({p.ParameterType.Name})"))
            .ToList();

        Assert.Empty(offenders);
        Assert.Null(typeof(AppointmentNoteChangeRequest).GetProperty("CompanyId"));
    }

    #endregion
}
