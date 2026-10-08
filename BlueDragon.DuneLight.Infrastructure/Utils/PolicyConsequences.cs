using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// P1 (ADR-0017) — JEDINO mjesto koje čita ledger posljedica politike sudjelovanja (pisanje isključivo kroz
/// IParticipationPolicyService). Najviše jedna Active posljedica po sudjelovanju (djelomični unique indeks).
/// </summary>
public static class PolicyConsequences
{
    public static ParticipationPolicyConsequence ActiveOf(BookingSegmentParticipation participation) =>
        participation.PolicyConsequences.SingleOrDefault(c => c.Status == PolicyConsequenceStatus.Active);

    /// <summary>Najnovija posljedica (po SourceVersion) bez obzira na stanje, ili null.</summary>
    public static ParticipationPolicyConsequence LatestOf(BookingSegmentParticipation participation) =>
        participation.PolicyConsequences.OrderByDescending(c => c.SourceVersion).FirstOrDefault();

    /// <summary>Aktivna potrošnja paketa vezana uz ovu posljedicu (kazna podmirena jedinicom), ili null.</summary>
    public static PackageConsumption ActiveConsumptionOf(BookingSegmentParticipation participation, ParticipationPolicyConsequence consequence) =>
        participation.PackageConsumptions.SingleOrDefault(c =>
            c.Status == PackageConsumptionStatus.Consumed &&
            c.Trigger == PackageConsumptionTrigger.PolicyConsequence &&
            c.ParticipationPolicyConsequenceId == consequence.Id);

    /// <summary>D12 — posljedica sa STVARNIM učinkom: naknada &gt; 0 ili aktivna povezana potrošnja paketa. Njezino poništenje
    /// korekcijom traži appointments.policy.override i razlog; posljedica bez učinka traži samo normalan pristup.</summary>
    public static bool HasRealEffect(BookingSegmentParticipation participation, ParticipationPolicyConsequence consequence) =>
        consequence.CalculatedFeeAmount > 0m || ActiveConsumptionOf(participation, consequence) != null
        || consequence.MembershipCreditForfeited;

    public static ParticipationPolicyConsequenceDto ToDto(ParticipationPolicyConsequence c) => c == null
        ? null
        : new ParticipationPolicyConsequenceDto
        {
            Id = c.Id,
            SourceVersion = c.SourceVersion,
            Event = c.Event,
            FeeType = c.FeeType,
            ConfiguredFeeValue = c.ConfiguredFeeValue,
            FeeBaseAmount = c.FeeBaseAmount,
            CalculatedFeeAmount = c.CalculatedFeeAmount,
            WasFeeCapped = c.WasFeeCapped,
            PolicyId = c.CancellationPolicyId,
            PolicyVersion = c.CancellationPolicyVersion,
            PackageAction = c.PackageAction,
            PackageUnitConsumed = c.PackageUnitConsumed,
            ClientPackageId = c.ClientPackageId,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            CreatedBy = c.CreatedBy,
            ReversedAt = c.ReversedAt,
            ReversedBy = c.ReversedBy,
            ReversalReason = c.ReversalReason,
            WaivedAt = c.WaivedAt,
            WaivedBy = c.WaivedBy,
            WaiverReason = c.WaiverReason,
            MembershipAction = c.MembershipAction,
            ClientMembershipId = c.ClientMembershipId,
            MembershipCreditForfeited = c.MembershipCreditForfeited
        };
}
