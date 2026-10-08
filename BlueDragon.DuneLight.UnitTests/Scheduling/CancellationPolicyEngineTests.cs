#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Events;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Outbox;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// P1 — Cancellation / Late Cancellation / NoShow policy engine (ADR-0015 – ADR-0018, docs/p1/P1_DECISION_RECORD.md).
/// Pins: profile resolution precedence and version selection (D1/D11), initiator and timing guards (D2/D3), the consequence
/// ledger and the status-aware settlement (D4/D5/D7), the package penalty selection (D6), no commission (D8), waivers and
/// corrections (D10/D12) and the policy management rules (D11).
/// </summary>
public class CancellationPolicyEngineTests
{
    private static readonly DateOnly LongValid = new(2035, 1, 1);

    /// <summary>A window that makes a client cancellation of <see cref="SchedulingWorld.Future"/>(10) LATE (now is years before).</summary>
    private static int LateWindow() => (int)(SchedulingWorld.Future(10) - DateTimeOffset.UtcNow).TotalMinutes + 60;

    private static ICancellationPolicyService Policies(SchedulingWorld w) => w.Resolve<ICancellationPolicyService>();

    private static CancellationPolicyRulesRequest Rules(int window,
        CancellationFeeType lateFee = CancellationFeeType.None, decimal? lateValue = null, CancellationPackageAction latePackage = CancellationPackageAction.None,
        CancellationFeeType noShowFee = CancellationFeeType.None, decimal? noShowValue = null, CancellationPackageAction noShowPackage = CancellationPackageAction.None) => new()
    {
        CancellationWindowMinutes = window,
        LateCancellation = new CancellationPolicyEventRuleDto { FeeType = lateFee, FeeValue = lateValue, PackageAction = latePackage },
        NoShow = new CancellationPolicyEventRuleDto { FeeType = noShowFee, FeeValue = noShowValue, PackageAction = noShowPackage }
    };

    private static async Task<ResolvedCancellationPolicy> Resolve(SchedulingWorld w, Guid companyId, Guid serviceId)
    {
        await using IUnitOfWork uow = await w.Resolve<IUnitOfWorkFactory>().Begin();
        return await w.Resolve<ICancellationPolicyResolver>().ResolveCancellationPolicy(uow, w.OrganizationId, companyId, serviceId);
    }

    private static async Task<BookingParticipationDto> OnlyParticipation(SchedulingWorld w, Guid appointmentId) =>
        (await w.Appointments.GetById(w.OrganizationId, appointmentId)).Bookings.Single().Participations.Single();

    #region D1/D11 — resolution and management

    [Fact]
    public async Task Resolver_PrefersCompanyAndService_ThenService_ThenCompany_ThenTheOrganizationDefault_AlwaysTheLatestVersion()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Resolver_PrefersCompanyAndService_ThenService_ThenCompany_ThenTheOrganizationDefault_AlwaysTheLatestVersion));
        Guid company = w.Company.Id.Value, service = w.Service.Id.Value;
        ICancellationPolicyService policies = Policies(w);

        ResolvedCancellationPolicy neutral = await Resolve(w, company, service);
        Assert.Equal((w.DefaultPolicyId, 1, 1440, CancellationPolicyResolvedFrom.OrganizationDefault),
            (neutral.PolicyId, neutral.Version, neutral.CancellationWindowMinutes, neutral.ResolvedFrom));
        Assert.Equal((CancellationFeeType.None, CancellationPackageAction.None), (neutral.NoShow.FeeType, neutral.NoShow.PackageAction));

        CancellationPolicyDto byCompany = await policies.Create(w.OrganizationId, w.ActorUserId, new CancellationPolicyCreateRequest { Name = "Company", CancellationWindowMinutes = 10, LateCancellation = Rules(0).LateCancellation, NoShow = Rules(0).NoShow });
        CancellationPolicyDto byService = await policies.Create(w.OrganizationId, w.ActorUserId, new CancellationPolicyCreateRequest { Name = "Service", CancellationWindowMinutes = 20, LateCancellation = Rules(0).LateCancellation, NoShow = Rules(0).NoShow });
        CancellationPolicyDto byBoth = await policies.Create(w.OrganizationId, w.ActorUserId, new CancellationPolicyCreateRequest { Name = "Both", CancellationWindowMinutes = 30, LateCancellation = Rules(0).LateCancellation, NoShow = Rules(0).NoShow });

        await policies.Assign(w.OrganizationId, w.ActorUserId, new CancellationPolicyAssignmentRequest { CompanyId = company, CancellationPolicyId = byCompany.Id });
        Assert.Equal((byCompany.Id, CancellationPolicyResolvedFrom.Company), ((await Resolve(w, company, service)).PolicyId, (await Resolve(w, company, service)).ResolvedFrom));

        await policies.Assign(w.OrganizationId, w.ActorUserId, new CancellationPolicyAssignmentRequest { ServiceId = service, CancellationPolicyId = byService.Id });
        Assert.Equal(byService.Id, (await Resolve(w, company, service)).PolicyId);

        await policies.Assign(w.OrganizationId, w.ActorUserId, new CancellationPolicyAssignmentRequest { CompanyId = company, ServiceId = service, CancellationPolicyId = byBoth.Id });
        ResolvedCancellationPolicy both = await Resolve(w, company, service);
        Assert.Equal((byBoth.Id, 1, 30, CancellationPolicyResolvedFrom.CompanyAndService), (both.PolicyId, both.Version, both.CancellationWindowMinutes, both.ResolvedFrom));

        // D11: a new version publishes immediately; the assignment points to the profile, so the latest version applies.
        await policies.PublishVersion(w.OrganizationId, w.ActorUserId, byBoth.Id, Rules(45));
        ResolvedCancellationPolicy latest = await Resolve(w, company, service);
        Assert.Equal((2, 45), (latest.Version, latest.CancellationWindowMinutes));

        // Another company falls through to the Service-only assignment (no silent fall-through past a matching scope).
        Company other = await w.AddCompany("Other");
        Assert.Equal(byService.Id, (await Resolve(w, other.Id.Value, service)).PolicyId);
    }

    [Fact]
    public async Task Management_DefaultOrAssignedCannotBeDeactivated_InactiveCannotBeAssignedOrMadeDefault_VersionsAreImmutable()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Management_DefaultOrAssignedCannotBeDeactivated_InactiveCannotBeAssignedOrMadeDefault_VersionsAreImmutable));
        ICancellationPolicyService policies = Policies(w);

        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationPolicyInUse, () => policies.Deactivate(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId));

        CancellationPolicyDto strict = await policies.Create(w.OrganizationId, w.ActorUserId, new CancellationPolicyCreateRequest
        {
            Name = "Strict", CancellationWindowMinutes = 120,
            LateCancellation = Rules(0, CancellationFeeType.Fixed, 10m).LateCancellation, NoShow = Rules(0).NoShow
        });
        CancellationPolicyAssignmentDto assignment = await policies.Assign(w.OrganizationId, w.ActorUserId,
            new CancellationPolicyAssignmentRequest { ServiceId = w.Service.Id, CancellationPolicyId = strict.Id });
        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationPolicyInUse, () => policies.Deactivate(w.OrganizationId, w.ActorUserId, strict.Id));

        await policies.RemoveAssignment(w.OrganizationId, assignment.Id);
        CancellationPolicyDto inactive = await policies.Deactivate(w.OrganizationId, w.ActorUserId, strict.Id);
        Assert.False(inactive.IsActive);
        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationPolicyInactive, () => policies.Assign(w.OrganizationId, w.ActorUserId,
            new CancellationPolicyAssignmentRequest { CompanyId = w.Company.Id, CancellationPolicyId = strict.Id }));
        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationPolicyInactive, () => policies.SetOrganizationDefault(w.OrganizationId, w.ActorUserId,
            new CancellationPolicyDefaultRequest { CancellationPolicyId = strict.Id }));

        // Publishing never rewrites an older version.
        await policies.Activate(w.OrganizationId, w.ActorUserId, strict.Id);
        CancellationPolicyDto republished = await policies.PublishVersion(w.OrganizationId, w.ActorUserId, strict.Id, Rules(60));
        Assert.Equal(new[] { 2, 1 }, republished.Versions.Select(v => v.Version).ToArray());
        Assert.Equal(120, republished.Versions.Single(v => v.Version == 1).CancellationWindowMinutes);
        Assert.Equal(CancellationFeeType.Fixed, republished.Versions.Single(v => v.Version == 1).LateCancellation.FeeType);

        // Moving the default: exactly one default afterwards; the previous default may now be deactivated.
        await policies.SetOrganizationDefault(w.OrganizationId, w.ActorUserId, new CancellationPolicyDefaultRequest { CancellationPolicyId = strict.Id });
        List<CancellationPolicyDto> all = await policies.GetAll(w.OrganizationId);
        Assert.Equal(strict.Id, Assert.Single(all, p => p.IsOrganizationDefault).Id);
        Assert.False((await policies.Deactivate(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId)).IsActive);
    }

    [Fact]
    public async Task Management_RejectsInvalidRules()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Management_RejectsInvalidRules));
        ICancellationPolicyService policies = Policies(w);

        foreach (CancellationPolicyRulesRequest invalid in new[]
                 {
                     Rules(-1),
                     Rules(0, CancellationFeeType.None, 5m),
                     Rules(0, CancellationFeeType.Fixed, null),
                     Rules(0, CancellationFeeType.Fixed, 10.005m),
                     Rules(0, CancellationFeeType.Percentage, 100.01m),
                     Rules(0, noShowFee: CancellationFeeType.Percentage, noShowValue: -1m)
                 })
            await SchedulingAssert.Validation(() => policies.PublishVersion(w.OrganizationId, w.ActorUserId, w.DefaultPolicyId, invalid));

        Assert.Single((await policies.GetById(w.OrganizationId, w.DefaultPolicyId)).Versions);
    }

    #endregion

    #region D4 — fee arithmetic

    [Theory]
    [InlineData(CancellationFeeType.None, null, "50", "0", false)]
    [InlineData(CancellationFeeType.Fixed, "20", "50", "20", false)]
    [InlineData(CancellationFeeType.Fixed, "80", "50", "50", true)]
    [InlineData(CancellationFeeType.Percentage, "33.33", "10", "3.33", false)]
    [InlineData(CancellationFeeType.Percentage, "12.5", "0.12", "0.02", false)]   // 0.015 rounds AwayFromZero
    [InlineData(CancellationFeeType.Percentage, "100", "49.99", "49.99", false)]
    [InlineData(CancellationFeeType.Fixed, "10", "0", "0", true)]
    public void Fee_IsCappedAtTheFinalPrice_AndRoundedAwayFromZero(CancellationFeeType type, string value, string baseAmount, string expected, bool capped)
    {
        decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        (decimal fee, bool wasCapped) = CancellationPolicyRules.CalculateFee(D(baseAmount), type, value == null ? null : D(value));

        Assert.Equal(D(expected), fee);
        Assert.Equal(capped, wasCapped);
        Assert.True(fee <= D(baseAmount));
    }

    #endregion

    #region D2/D3 — initiator, lateness and timing guards

    [Fact]
    public async Task ClientCancel_InsideTheWindow_IsLate_CreatesTheConsequence_AndTheFeeIsTheOnlyDue()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientCancel_InsideTheWindow_IsLate_CreatesTheConsequence_AndTheFeeIsTheOnlyDue));
        await w.AddCommissionRule(w.Employee, w.Service, CommissionCalculationType.Percentage, 10m);
        int window = LateWindow();
        await w.PublishDefaultPolicyVersion(window, CancellationFeeType.Percentage, 40m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "sick");

        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((CancellationInitiator.Client, true, w.DefaultPolicyId, 2), (p.CancellationInitiator.Value, p.IsLateCancellation.Value, p.CancellationPolicyId.Value, p.CancellationPolicyVersion.Value));
        Assert.Equal(window, p.AppliedCancellationWindowMinutes.Value); // the snapshotted window of version 2
        Assert.Equal((50m, 20m, 20m, 0m), (p.Amount, p.MonetaryDue, p.OutstandingAmount, p.SurplusAmount)); // Amount is never replaced

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal((PolicyConsequenceEvent.LateCancellation, CancellationFeeType.Percentage, 40m, 50m, 20m, false),
            (c.Event, c.FeeType, c.ConfiguredFeeValue.Value, c.FeeBaseAmount, c.CalculatedFeeAmount, c.WasFeeCapped));
        Assert.Equal((1, PolicyConsequenceStatus.Active, 2), (c.SourceVersion, c.Status, c.CancellationPolicyVersion));
        Assert.Empty(await w.LoadCommissionEntries()); // D8: policy events never earn commission
        OutboxMessage message = Assert.Single(await w.LoadOutbox());
        BookingCancelledEvent payload = System.Text.Json.JsonSerializer.Deserialize<BookingCancelledEvent>(
            message.Payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Equal(CancellationInitiator.Client, payload.CancellationInitiator); // D2: the event carries the initiator
    }

    [Fact]
    public async Task ClientCancel_OutsideTheWindow_IsOnTime_AndCreatesNoConsequence_EvenWithAWaiverFlag()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientCancel_OutsideTheWindow_IsOnTime_AndCreatesNoConsequence_EvenWithAWaiverFlag));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride);
        await w.PublishDefaultPolicyVersion(0, CancellationFeeType.Fixed, 30m); // window 0: every valid client cancel is on time
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, waivePolicyConsequence: true, waiverReason: "goodwill");

        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((false, 0), (p.IsLateCancellation.Value, p.AppliedCancellationWindowMinutes.Value));
        Assert.Empty(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal(0m, p.MonetaryDue);
    }

    [Fact]
    public async Task ClientCancel_AtOrAfterStart_IsRejected_ButBusinessMayCancelAfterStart_WithoutAnyClassification()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(ClientCancel_AtOrAfterStart_IsRejected_ButBusinessMayCancelAfterStart_WithoutAnyClassification));
        await w.PublishDefaultPolicyVersion(1440, CancellationFeeType.Fixed, 30m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationAfterStart,
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled));
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);

        // Business needs appointments.write.all and a reason; it is never classified and creates no consequence.
        await Assert.ThrowsAsync<ForbiddenAppException>(
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, initiator: CancellationInitiator.Business));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll);
        await SchedulingAssert.Validation(() => w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true,
            created.Bookings.Single().Participations.Single().Id,
            new BookingSetStatusRequest { Status = BookingStatus.Cancelled, CancellationInitiator = CancellationInitiator.Business }));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "trainer ill", initiator: CancellationInitiator.Business);

        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((CancellationInitiator.Business, "trainer ill"), (p.CancellationInitiator.Value, p.CancellationReason));
        Assert.Null(p.IsLateCancellation);
        Assert.Null(p.CancellationPolicyId);
        Assert.Empty(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal(0m, p.MonetaryDue);
    }

    [Fact]
    public async Task Requests_WithoutAnInitiator_OrWithSystem_AreRejected_AndAppointmentWideCancelIsBusinessOnly()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Requests_WithoutAnInitiator_OrWithSystem_AreRejected_AndAppointmentWideCancelIsBusinessOnly));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid participationId = created.Bookings.Single().Participations.Single().Id;

        foreach (CancellationInitiator? initiator in new CancellationInitiator?[] { null, CancellationInitiator.System })
            await SchedulingAssert.Validation(() => w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
                new BookingSetStatusRequest { Status = BookingStatus.Cancelled, CancellationInitiator = initiator, CancellationReason = "x" }));
        await SchedulingAssert.Validation(() => w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            new BookingCancelRequest { CancellationReason = "x" }));
        await SchedulingAssert.Validation(() => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationInitiator = CancellationInitiator.Client, CancellationReason = "x" }));
        await SchedulingAssert.Validation(() => w.Appointments.Cancel(w.OrganizationId, w.ActorUserId, true, created.Id,
            new AppointmentCancelRequest { CancellationInitiator = CancellationInitiator.Business }));

        Assert.Equal(0, (await w.LoadBooking(created.Id, w.Client)).StatusVersion);
    }

    [Fact]
    public async Task AppointmentWideNoShow_IsAtomic_WhenOneActiveSegmentHasNotStarted_NothingChanges()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentWideNoShow_IsAtomic_WhenOneActiveSegmentHasNotStarted_NothingChanges));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), 30m); // a segment that has not started

        await SchedulingAssert.BusinessRule(ErrorCodes.AttendanceBeforeStart,
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id, new NoShowRequest()));

        Assert.All(await w.LoadParticipations(created.Id, w.Client), p => Assert.Equal((ParticipationStatus.Confirmed, 0), (p.Status, p.StatusVersion)));
        Assert.Empty(await w.LoadOutbox());
    }

    #endregion

    #region D4/D5/D7 — no-show consequence, settlement, surplus, checkout

    [Fact]
    public async Task NoShow_WithAFixedFee_DueIsTheFee_TheFeeCanBeSettledThroughCheckout_AndSurplusIsInformational()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NoShow_WithAFixedFee_DueIsTheFee_TheFeeCanBeSettledThroughCheckout_AndSurplusIsInformational));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 15m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid bookingId = created.Bookings.Single().Id;

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "absent");

        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((15m, 15m, 0m, false), (p.MonetaryDue, p.OutstandingAmount, p.SurplusAmount, p.IsPaid));
        Assert.Equal((PolicyConsequenceEvent.NoShow, 15m), (p.PolicyConsequence.Event, p.PolicyConsequence.CalculatedFeeAmount));

        // D5: checkout eligibility is financial — a NoShow with a positive fee can be settled (the item is the fee).
        await w.PayBookingViaCheckout(bookingId, w.Client, 15m);
        CheckoutItem item = Assert.Single(await w.LoadCheckoutItems(bookingId));
        Assert.Equal(15m, item.Amount);
        p = await OnlyParticipation(w, created.Id);
        Assert.Equal((15m, 0m, true), (p.PaidAmount, p.OutstandingAmount, p.IsPaid));

        // Once the fee is settled nothing is payable: a terminal participation is no longer checkout-eligible.
        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutItemNotEligible, () => w.PayBookingViaCheckout(bookingId, w.Client, 1m));
    }

    [Fact]
    public async Task NoShow_UnderTheNeutralDefault_OwesNothing_SoCheckoutRefusesIt_AndPrepaidMoneyBecomesSurplus()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(NoShow_UnderTheNeutralDefault_OwesNothing_SoCheckoutRefusesIt_AndPrepaidMoneyBecomesSurplus));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.PayBookingViaCheckout(bookingId, w.Client, 50m);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        // D11: the neutral default still records the evaluated event (FeeType None, fee 0) — "evaluated, no fee".
        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal((CancellationFeeType.None, 0m, PolicyConsequenceStatus.Active), (c.FeeType, c.CalculatedFeeAmount, c.Status));
        BookingDto booking = (await w.Appointments.GetById(w.OrganizationId, created.Id)).Bookings.Single();
        Assert.Equal((50m, 0m, 50m, true), (booking.PaidAmount, booking.OutstandingAmount, booking.SurplusAmount, booking.IsPaid));
        Assert.Equal(-50m, booking.Participations.Single().OutstandingAmount); // the raw (unclamped) debt lives on the participation
        Assert.Equal(PaymentStatus.Completed, Assert.Single(await w.LoadPayments(bookingId)).Status); // D7: no money moves
        await SchedulingAssert.BusinessRule(ErrorCodes.CheckoutItemNotEligible, () => w.PayBookingViaCheckout(bookingId, w.Client, 1m));
    }

    [Fact]
    public async Task Correction_FromAFeeConsequenceToConfirmed_NeedsTheOverrideAndAReason_ThenReversesIt_AndTheServiceDueReturns()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(Correction_FromAFeeConsequenceToConfirmed_NeedsTheOverrideAndAReason_ThenReversesIt_AndTheServiceDueReturns));
        await w.PublishDefaultPolicyVersion(LateWindow(), CancellationFeeType.Fixed, 10m);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid bookingId = created.Bookings.Single().Id;
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        await w.PayBookingViaCheckout(bookingId, w.Client, 10m); // the fee is paid

        await SchedulingAssert.Validation(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "client came after all"));
        Assert.Equal(BookingStatus.Cancelled, (await w.LoadBooking(created.Id, w.Client)).Status);

        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "client came after all");

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal((PolicyConsequenceStatus.Reversed, "client came after all", w.ActorUserId), (c.Status, c.ReversalReason, c.ReversedBy.Value));
        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        // D7 example: the paid fee stays attached as a prepayment against the restored service due.
        Assert.Equal((BookingStatus.Confirmed, 2, 50m, 10m, 40m), (p.Status, p.StatusVersion, p.MonetaryDue, p.PaidAmount, p.OutstandingAmount));
        Assert.Null(p.CancellationInitiator);
    }

    [Fact]
    public async Task TerminalToTerminal_IsOneAtomicCorrection_NoShowToBusinessCancel_ReversesTheZeroEffectConsequence_WithoutOverride()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(TerminalToTerminal_IsOneAtomicCorrection_NoShowToBusinessCancel_ReversesTheZeroEffectConsequence_WithoutOverride));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsWriteAll);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, "absent");

        // NoShow -> Cancelled is Business only (a client cancel after start is CANCELLATION_AFTER_START).
        await SchedulingAssert.BusinessRule(ErrorCodes.CancellationAfterStart, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled, "studio error", initiator: CancellationInitiator.Business);

        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((BookingStatus.Cancelled, 2), (p.Status, p.StatusVersion)); // one increment for the whole correction
        Assert.Null(p.NoShowAt);                                                 // previous metadata cleared
        Assert.Null(p.NoShowReason);
        Assert.Equal(PolicyConsequenceStatus.Reversed, Assert.Single(await w.LoadPolicyConsequences(created.Id)).Status);
        // A notification-producing event for every entry into Cancelled/NoShow (decision 2026-10-06).
        Assert.Equal(new[] { OutboxEventTypes.BookingNoShowV1, OutboxEventTypes.BookingCancelledV1 },
            (await w.LoadOutbox()).OrderBy(m => m.OccurredAt).Select(m => m.Type).ToArray());
    }

    #endregion

    #region D6 — package penalty

    [Fact]
    public async Task PackagePenalty_OneCountedPackage_IsConsumedInsteadOfTheFee_AndReturnedWhenTheConsequenceIsReversed()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackagePenalty_OneCountedPackage_IsConsumedInsteadOfTheFee_AndReturnedWhenTheConsequenceIsReversed));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        ClientPackage unlimited = await w.AddClientPackage(w.Client, w.Service, null, LongValid); // never a penalty source
        ClientPackage counted = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal((true, counted.Id.Value, 20m), (c.PackageUnitConsumed, c.ClientPackageId.Value, c.CalculatedFeeAmount));
        PackageConsumption consumption = Assert.Single(await w.LoadPackageConsumptions(created.Id));
        Assert.Equal((PackageConsumptionTrigger.PolicyConsequence, c.Id, 1), (consumption.Trigger, consumption.ParticipationPolicyConsequenceId.Value, consumption.Units));
        Assert.Equal(4, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);
        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((0m, false), (p.MonetaryDue, p.PackageCovered)); // unit and fee are alternatives; the unit is not service coverage
        Assert.Null((await w.LoadClientPackage(unlimited.Id.Value)).ServiceEntries.Single().RemainingEntries);

        // Correction back to Confirmed reverses the consequence (real effect: override + reason) and returns the unit.
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride);
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed, correctionReason: "recorded by mistake");
        Assert.Equal(5, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal((PackageConsumptionStatus.Reversed, PackageConsumptionReversalReason.PolicyConsequenceReversed),
            ((await w.LoadPackageConsumptions(created.Id)).Single().Status, (await w.LoadPackageConsumptions(created.Id)).Single().ReversalReason));
    }

    [Fact]
    public async Task PackagePenalty_SeveralCountedPackages_RequireAnExplicitChoice_WhichMustBeEligible()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackagePenalty_SeveralCountedPackages_RequireAnExplicitChoice_WhichMustBeEligible));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        ClientPackage first = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        ClientPackage second = await w.AddClientPackage(w.Client, w.Service, 3, LongValid);
        ServiceEntity otherService = await w.AddService(30, 10m, name: "Other");
        ClientPackage foreign = await w.AddClientPackage(w.Client, otherService, 3, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageSelectionRequired, () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow));
        await SchedulingAssert.BusinessRule(ErrorCodes.PackageNotEligible,
            () => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, clientPackageId: foreign.Id));
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status); // nothing was applied

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, clientPackageId: second.Id);

        Assert.Equal(second.Id, Assert.Single(await w.LoadPolicyConsequences(created.Id)).ClientPackageId);
        Assert.Equal((5, 2), ((await w.LoadClientPackage(first.Id.Value)).ServiceEntries.Single().RemainingEntries.Value,
            (await w.LoadClientPackage(second.Id.Value)).ServiceEntries.Single().RemainingEntries.Value));
    }

    [Fact]
    public async Task PackagePenalty_AnActiveMonetarySettlement_FallsBackToTheFee_NeverAsksForASelection()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(PackagePenalty_AnActiveMonetarySettlement_FallsBackToTheFee_NeverAsksForASelection));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        await w.PayBookingViaCheckout(created.Bookings.Single().Id, w.Client, 5m);

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.False(c.PackageUnitConsumed);
        Assert.Empty(await w.LoadPackageConsumptions(created.Id));
        BookingParticipationDto p = await OnlyParticipation(w, created.Id);
        Assert.Equal((20m, 5m, 15m), (p.MonetaryDue, p.PaidAmount, p.OutstandingAmount));
    }

    #endregion

    #region D6 — package selection per participation (multi-participation commands)

    [Fact]
    public async Task AppointmentWideNoShow_ChoosesThePenaltyPackagePerParticipation_AndRejectsASingleClientPackageId()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(AppointmentWideNoShow_ChoosesThePenaltyPackagePerParticipation_AndRejectsASingleClientPackageId));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        ClientPackage first = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        ClientPackage second = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid participationId = created.Bookings.Single().Participations.Single().Id;

        await SchedulingAssert.BusinessRule(ErrorCodes.PackageSelectionRequired,
            () => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id, new NoShowRequest()));
        // One package id for a whole appointment is never applied to everyone (nor silently ignored).
        await SchedulingAssert.Validation(() => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id,
            new NoShowRequest { ClientPackageId = second.Id }));
        await SchedulingAssert.Validation(() => w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id,
            new NoShowRequest { PackageSelections = { new ParticipationPackageSelection { ParticipationId = Guid.NewGuid(), ClientPackageId = second.Id } } }));
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(created.Id, w.Client)).Status);

        await w.Appointments.MarkNoShow(w.OrganizationId, w.ActorUserId, true, created.Id, new NoShowRequest
        {
            PackageSelections = { new ParticipationPackageSelection { ParticipationId = participationId, ClientPackageId = second.Id } }
        });

        Assert.Equal(second.Id, Assert.Single(await w.LoadPolicyConsequences(created.Id)).ClientPackageId);
        Assert.Equal((5, 4), ((await w.LoadClientPackage(first.Id.Value)).ServiceEntries.Single().RemainingEntries.Value,
            (await w.LoadClientPackage(second.Id.Value)).ServiceEntries.Single().RemainingEntries.Value));
    }

    [Fact]
    public async Task BookingWideClientCancel_ChoosesThePenaltyPackagePerParticipation()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(BookingWideClientCancel_ChoosesThePenaltyPackagePerParticipation));
        await w.PublishDefaultPolicyVersion(LateWindow() + 600, CancellationFeeType.Fixed, 10m, CancellationPackageAction.ConsumeUnit);
        ClientPackage a = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        ClientPackage b = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        Guid first = created.Bookings.Single().Participations.Single().Id;
        Guid second = await w.AddArtificialSegmentParticipation(created.Id, w.Client, SchedulingWorld.Future(14), 50m);

        await SchedulingAssert.Validation(() => w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value,
            new BookingCancelRequest { CancellationInitiator = CancellationInitiator.Client, ClientPackageId = a.Id }));

        await w.Bookings.CancelBooking(w.OrganizationId, w.ActorUserId, true, created.Id, w.Client.Id.Value, new BookingCancelRequest
        {
            CancellationInitiator = CancellationInitiator.Client,
            PackageSelections =
            {
                new ParticipationPackageSelection { ParticipationId = first, ClientPackageId = a.Id },
                new ParticipationPackageSelection { ParticipationId = second, ClientPackageId = b.Id }
            }
        });

        List<ParticipationPolicyConsequence> consequences = await w.LoadPolicyConsequences(created.Id);
        Assert.Equal(a.Id, consequences.Single(c => c.BookingSegmentParticipationId == first).ClientPackageId);
        Assert.Equal(b.Id, consequences.Single(c => c.BookingSegmentParticipationId == second).ClientPackageId);
    }

    #endregion

    #region Booking note and confirm body

    [Fact]
    public async Task SameStatusRetry_IsANoOp_ButStillStoresAnExplicitNewNote()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(SameStatusRetry_IsANoOp_ButStillStoresAnExplicitNewNote));
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));
        Guid participationId = created.Bookings.Single().Participations.Single().Id;
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow);

        await w.Bookings.SetParticipationStatus(w.OrganizationId, w.ActorUserId, true, participationId,
            new BookingSetStatusRequest { Status = BookingStatus.NoShow, Note = "called later" });

        Booking b = await w.LoadBooking(created.Id, w.Client);
        Assert.Equal(("called later", 1), (b.Note, b.StatusVersion));
        Assert.Single(await w.LoadOutbox());
        Assert.Single(await w.LoadPolicyConsequences(created.Id));
    }

    #endregion

    #region D10 — waiver

    [Fact]
    public async Task WaiverAtEventTime_NeedsTheOverrideAndAReason_RecordsAWaivedConsequence_WithoutConsumingAPackage()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaiverAtEventTime_NeedsTheOverrideAndAReason_RecordsAWaivedConsequence_WithoutConsumingAPackage));
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Fixed, noShowFeeValue: 20m, noShowPackageAction: CancellationPackageAction.ConsumeUnit);
        ClientPackage counted = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Past(10));

        await Assert.ThrowsAsync<ForbiddenAppException>(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow,
            waivePolicyConsequence: true, waiverReason: "first visit"));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride);
        await SchedulingAssert.Validation(() => w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, waivePolicyConsequence: true));

        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.NoShow, waivePolicyConsequence: true, waiverReason: "first visit");

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(created.Id));
        Assert.Equal((PolicyConsequenceStatus.Waived, "first visit", 20m, false), (c.Status, c.WaiverReason, c.CalculatedFeeAmount, c.PackageUnitConsumed));
        Assert.Empty(await w.LoadPackageConsumptions(created.Id));
        Assert.Equal(5, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(0m, (await OnlyParticipation(w, created.Id)).MonetaryDue);
        Assert.Single(await w.LoadAuditLog(created.Id), l => l.ChangeType == "PolicyConsequenceWaived");
    }

    [Fact]
    public async Task WaiverAfterTheEvent_ReturnsThePenaltyUnit_KeepsTheClassification_AndIsFinal()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(WaiverAfterTheEvent_ReturnsThePenaltyUnit_KeepsTheClassification_AndIsFinal));
        await w.PublishDefaultPolicyVersion(LateWindow(), CancellationFeeType.Fixed, 20m, CancellationPackageAction.ConsumeUnit);
        ClientPackage counted = await w.AddClientPackage(w.Client, w.Service, 5, LongValid);
        AppointmentDto created = await w.CreateAppointment(SchedulingWorld.Future(10));
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Cancelled);
        Guid participationId = created.Bookings.Single().Participations.Single().Id;
        Assert.Equal(4, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);

        await Assert.ThrowsAsync<ForbiddenAppException>(() => w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, participationId,
            new PolicyConsequenceWaiveRequest { WaiverReason = "goodwill" }));
        await w.GrantUser(w.ActorUserId, Grants.AppointmentsPolicyOverride, Grants.AppointmentsWriteAll);

        BookingDto dto = await w.Bookings.WaivePolicyConsequence(w.OrganizationId, w.ActorUserId, participationId,
            new PolicyConsequenceWaiveRequest { WaiverReason = "goodwill" });

        BookingParticipationDto p = dto.Participations.Single();
        Assert.Equal((PolicyConsequenceStatus.Waived, true, 0m), (p.PolicyConsequence.Status, p.IsLateCancellation.Value, p.MonetaryDue));
        Assert.Equal(5, (await w.LoadClientPackage(counted.Id.Value)).ServiceEntries.Single().RemainingEntries);
        Assert.Equal(PackageConsumptionReversalReason.PolicyConsequenceWaived, Assert.Single(await w.LoadPackageConsumptions(created.Id)).ReversalReason);
        Assert.Equal(1, p.StatusVersion); // a waiver is not a status transition
        await SchedulingAssert.BusinessRule(ErrorCodes.NoActivePolicyConsequence, () => w.Bookings.WaivePolicyConsequence(
            w.OrganizationId, w.ActorUserId, participationId, new PolicyConsequenceWaiveRequest { WaiverReason = "again" }));

        // A later correction to Confirmed leaves the Waived record Waived (and needs no override: nothing active is reversed).
        await w.SetBookingStatus(created.Id, w.Client, BookingStatus.Confirmed);
        Assert.Equal(PolicyConsequenceStatus.Waived, Assert.Single(await w.LoadPolicyConsequences(created.Id)).Status);
    }

    #endregion

    #region D9 — group

    [Fact]
    public async Task GroupAttendance_AttendedFalse_IsTheNoShowPolicy_AndCloseOutNeverCreatesNoShows()
    {
        await using SchedulingWorld w = await SchedulingWorld.Create(nameof(GroupAttendance_AttendedFalse_IsTheNoShowPolicy_AndCloseOutNeverCreatesNoShows));
        ServiceEntity svc = await w.AddGroupService();
        var group = await w.CreateGroup(svc, capacity: 3);
        Client second = await w.AddClient("Second", "Member");
        await w.AddGroupMember(group, w.Client);
        await w.AddGroupMember(group, second);
        Appointment occurrence = await w.GenerateSingleOccurrence(group);
        await w.MoveToPast(occurrence.Id.Value);
        await w.PublishDefaultPolicyVersion(1440, noShowFeeType: CancellationFeeType.Percentage, noShowFeeValue: 50m);

        await w.GroupAttendance.SetAttendance(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value, new Core.DTOs.Groups.SetGroupAttendanceRequest
        {
            ClientId = w.Client.Id.Value, SegmentId = Assert.Single(occurrence.Segments).Id, Attended = false, NoShowReason = "absent"
        });
        AppointmentDto closed = await w.Appointments.CompleteGroupAppointment(w.OrganizationId, w.ActorUserId, true, occurrence.Id.Value);

        ParticipationPolicyConsequence c = Assert.Single(await w.LoadPolicyConsequences(occurrence.Id.Value));
        Assert.Equal((PolicyConsequenceEvent.NoShow, 7.5m), (c.Event, c.CalculatedFeeAmount)); // 50 % of the group price 15
        Assert.Equal(BookingStatus.Confirmed, (await w.LoadBooking(occurrence.Id.Value, second)).Status); // no automatic NoShow
        SchedulingAssert.HasWarning(closed, WarningCodes.GroupAppointmentUnresolvedBookings);
    }

    #endregion
}
