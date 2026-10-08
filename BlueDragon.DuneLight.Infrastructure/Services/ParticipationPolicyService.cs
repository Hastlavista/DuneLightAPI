using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi IParticipationPolicyService.</summary>
public class ParticipationPolicyService : IParticipationPolicyService
{
    private readonly ICancellationPolicyResolver _resolver;
    private readonly IClientPackageService _clientPackageService;
    private readonly IPackageConsumptionLedgerService _packageConsumptionLedgerService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IAppointmentAuditLogHandler _auditLogHandler;
    private readonly IMembershipCoverageService _membershipCoverage;

    public ParticipationPolicyService(
        ICancellationPolicyResolver resolver,
        IClientPackageService clientPackageService,
        IPackageConsumptionLedgerService packageConsumptionLedgerService,
        ICheckoutHandler checkoutHandler,
        IAppointmentAuditLogHandler auditLogHandler,
        IMembershipCoverageService membershipCoverage)
    {
        _resolver = resolver;
        _clientPackageService = clientPackageService;
        _packageConsumptionLedgerService = packageConsumptionLedgerService;
        _checkoutHandler = checkoutHandler;
        _auditLogHandler = auditLogHandler;
        _membershipCoverage = membershipCoverage;
    }

    public async Task ApplyClientCancellation(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, DateTimeOffset eventAt, PolicyEventOptions options)
    {
        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);
        // D11: razrješava se točno jednom, u trenutku događaja, iz trenutnog konteksta (poslovnica termina + usluga segmenta).
        ResolvedCancellationPolicy policy = await _resolver.ResolveCancellationPolicy(uow, organizationId, execution.CompanyId, execution.ServiceId);

        bool isLate = CancellationPolicyRules.IsLateCancellation(execution.StartsAt, eventAt, policy.CancellationWindowMinutes);
        ParticipationEventMetadata.SetClientClassification(participation, isLate, policy.PolicyId, policy.Version, policy.CancellationWindowMinutes);

        // D4: na vrijeme = nema posljedice (nije konfigurabilno).
        if (isLate)
            await CreateConsequence(uow, organizationId, userId, appointment, booking, participation, execution, policy,
                PolicyConsequenceEvent.LateCancellation, policy.LateCancellation, eventAt, options);
    }

    public async Task ApplyNoShow(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, DateTimeOffset eventAt, PolicyEventOptions options)
    {
        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(appointment, booking, participation);
        ResolvedCancellationPolicy policy = await _resolver.ResolveCancellationPolicy(uow, organizationId, execution.CompanyId, execution.ServiceId);
        await CreateConsequence(uow, organizationId, userId, appointment, booking, participation, execution, policy,
            PolicyConsequenceEvent.NoShow, policy.NoShow, eventAt, options);
    }

    /// <summary>D4/D5/D6 — nepromjenjiv zapis posljedice (i za FeeType None), SourceVersion = StatusVersion događaja. Otpis u
    /// trenutku događaja: zapis nastaje izravno kao Waived, bez odabira i potrošnje paketa. Inače, uz PackageAction ConsumeUnit,
    /// jedinica brojenog paketa (alternativa naknadi) prema pravilima odabira D6.</summary>
    private async Task CreateConsequence(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, ParticipationExecutionContext execution, ResolvedCancellationPolicy policy,
        PolicyConsequenceEvent @event, PolicyEventRule rule, DateTimeOffset eventAt, PolicyEventOptions options)
    {
        (decimal fee, bool capped) = CancellationPolicyRules.CalculateFee(participation.Amount, rule.FeeType, rule.FeeValue);
        bool waive = options?.WaivePolicyConsequence == true;

        // P2 (Q26/Q31): sesija pokrivena članarinom (aktivan claim, pod lockom članstva). ForfeitCredit zadržava claim (kredit i
        // mjesta u prozorima potrošeni); na planu s kreditima perioda kredit propada UMJESTO naknade (jedinica ILI naknada, kao
        // P1 D6), a na planu bez kredita perioda naplaćuje se naknada. ReturnCreditChargeFee i otpis vraćaju claim (vraća ga
        // usklađivanje pokrića nakon događaja). Sesija bez claima (i klijent bez članarine): P1 bez ikakve promjene.
        MembershipActiveClaim membershipClaim = await _membershipCoverage.GetActiveClaim(uow, organizationId, participation,
            new ParticipationExecutionContextView(execution.ParticipationId, execution.ClientId, execution.ServiceId, execution.CompanyId, execution.StartsAt));
        bool retainsClaim = membershipClaim != null && !waive && rule.MembershipAction == CancellationMembershipAction.ForfeitCredit;
        bool creditForfeited = retainsClaim && membershipClaim.HasPeriodCredits;

        Guid? penaltyPackageId = null;
        if (!waive && !creditForfeited && rule.PackageAction == CancellationPackageAction.ConsumeUnit)
            penaltyPackageId = await SelectPenaltyPackage(uow, organizationId, participation, execution, options?.ClientPackageId);

        ParticipationPolicyConsequence consequence = new ParticipationPolicyConsequence
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            BookingSegmentParticipationId = participation.Id.GetValueOrDefault(),
            SourceVersion = participation.StatusVersion,
            Event = @event,
            FeeType = rule.FeeType,
            ConfiguredFeeValue = rule.FeeValue,
            FeeBaseAmount = participation.Amount,
            CalculatedFeeAmount = fee,
            WasFeeCapped = capped,
            CancellationPolicyId = policy.PolicyId,
            CancellationPolicyVersion = policy.Version,
            PackageAction = rule.PackageAction,
            PackageUnitConsumed = penaltyPackageId.HasValue,
            ClientPackageId = penaltyPackageId,
            Status = waive ? PolicyConsequenceStatus.Waived : PolicyConsequenceStatus.Active,
            CreatedAt = eventAt,
            CreatedBy = userId,
            WaivedAt = waive ? eventAt : null,
            WaivedBy = waive ? userId : null,
            WaiverReason = waive ? options.WaiverReason.Trim() : null,
            MembershipAction = membershipClaim != null ? rule.MembershipAction : null,
            ClientMembershipId = membershipClaim?.Membership.Id,
            MembershipUsageId = retainsClaim ? membershipClaim.Usage.Id : null,
            MembershipCreditForfeited = creditForfeited
        };
        uow.Context.ParticipationPolicyConsequences.Add(consequence);
        if (!participation.PolicyConsequences.Contains(consequence))
            participation.PolicyConsequences.Add(consequence);
        await uow.Context.SaveChangesAsync();

        if (penaltyPackageId.HasValue)
            await _packageConsumptionLedgerService.ConsumeForPolicyConsequence(
                uow, organizationId, userId, participation, execution, penaltyPackageId.Value, consequence.Id);

        if (waive)
            await WriteWaivedAudit(uow, appointment, booking, participation, consequence, userId, eventAt);
    }

    /// <summary>D6 — odabir paketa za kaznu (samo kad posljedica postoji, ConsumeUnit i isključivost dopušta paket):
    /// 1) aktivno novčano namirenje → bez paketa (naknada), PACKAGE_SELECTION_REQUIRED se tada nikad ne baca;
    /// 2) eksplicitni ClientPackageId → mora biti prihvatljiv brojeni paket (usluga, poslovnica, datum izvođenja), inače
    ///    PACKAGE_NOT_ELIGIBLE; 3) točno jedan prihvatljiv brojeni → automatski; 4) više → PACKAGE_SELECTION_REQUIRED;
    /// 5) nijedan → naknada. Neograničeni paketi nikad nisu izvor kazne.</summary>
    private async Task<Guid?> SelectPenaltyPackage(
        IUnitOfWork uow, Guid organizationId, BookingSegmentParticipation participation, ParticipationExecutionContext execution,
        Guid? requestedPackageId)
    {
        decimal settled = ParticipationSettlement.SettledAmountOf(
            await _checkoutHandler.GetItemsForParticipation(uow, organizationId, participation.Id.GetValueOrDefault()));
        if (settled > 0m)
            return null;

        List<ClientPackageDto> eligible = (await _clientPackageService.GetEligibleForService(
                organizationId, execution.ClientId, execution.ServiceId, execution.StartsAt, execution.CompanyId))
            .Where(p => PackageCounting.IsCounted(p, execution.ServiceId))
            .ToList();

        if (requestedPackageId.HasValue)
        {
            if (eligible.All(p => p.Id != requestedPackageId.Value))
                throw new BusinessRuleException(ErrorCodes.PackageNotEligible,
                    "Odabrani paket nije valjan brojeni paket klijenta za ovu uslugu, poslovnicu i datum.");
            return requestedPackageId.Value;
        }

        return eligible.Count switch
        {
            0 => null,
            1 => eligible[0].Id,
            _ => throw new BusinessRuleException(ErrorCodes.PackageSelectionRequired,
                "Klijent ima više prihvatljivih paketa — za kaznu politike potrebno je odabrati jedan (ClientPackageId).",
                new { clientPackageIds = eligible.Select(p => p.Id).ToList() })
        };
    }

    public async Task<bool> ReverseActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, string reason, DateTimeOffset at)
    {
        ParticipationPolicyConsequence active = PolicyConsequences.ActiveOf(participation);
        if (active == null)
            return false;

        if (PolicyConsequences.ActiveConsumptionOf(participation, active) != null)
            await _packageConsumptionLedgerService.ReverseActive(
                uow, organizationId, userId, participation, PackageConsumptionReversalReason.PolicyConsequenceReversed);

        active.Status = PolicyConsequenceStatus.Reversed;
        active.ReversedAt = at;
        active.ReversedBy = userId;
        active.ReversalReason = reason;
        await uow.Context.SaveChangesAsync();
        return true;
    }

    public async Task WaiveActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, Appointment appointment, Booking booking,
        BookingSegmentParticipation participation, string waiverReason, DateTimeOffset at)
    {
        ParticipationPolicyConsequence active = PolicyConsequences.ActiveOf(participation)
            ?? throw new BusinessRuleException(ErrorCodes.NoActivePolicyConsequence, "Sudjelovanje nema aktivnu posljedicu politike za otpis.");

        // D10: otpis vraća jedinicu potrošenu kao kaznu (zapis potrošnje ostaje), novac se ne pomiče.
        if (PolicyConsequences.ActiveConsumptionOf(participation, active) != null)
            await _packageConsumptionLedgerService.ReverseActive(
                uow, organizationId, userId, participation, PackageConsumptionReversalReason.PolicyConsequenceWaived);

        active.Status = PolicyConsequenceStatus.Waived;
        active.WaivedAt = at;
        active.WaivedBy = userId;
        active.WaiverReason = waiverReason.Trim();
        await uow.Context.SaveChangesAsync();

        await WriteWaivedAudit(uow, appointment, booking, participation, active, userId, at);
    }

    private Task WriteWaivedAudit(
        IUnitOfWork uow, Appointment appointment, Booking booking, BookingSegmentParticipation participation,
        ParticipationPolicyConsequence consequence, Guid userId, DateTimeOffset at)
    {
        return _auditLogHandler.Add(uow, new AppointmentAuditLog
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id.GetValueOrDefault(),
            BookingId = booking.Id,
            BookingSegmentParticipationId = participation.Id,
            ChangeType = "PolicyConsequenceWaived",
            OldValue = $"{consequence.Event}:{consequence.CalculatedFeeAmount.ToString(CultureInfo.InvariantCulture)}",
            // audit_log.new_value je varchar(255); puni razlog je na zapisu posljedice.
            NewValue = consequence.WaiverReason.Length > 255 ? consequence.WaiverReason[..255] : consequence.WaiverReason,
            StatusVersion = consequence.SourceVersion,
            ChangedAt = at,
            ChangedBy = userId
        });
    }
}
