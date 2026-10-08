using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// P1 (ADR-0017, D4/D5/D6) — NEPROMJENJIV zapis primijenjenog događaja politike (kasno klijentsko otkazivanje ili
/// izostanak) na jednom sudjelovanju, uključujući FeeType None i naknadu 0 ("evaluirano, bez naknade" nije isto što i
/// "nije se primijenilo"). Snapshot pravila i izračuna je fizički ovdje. Ledger (isti obrazac kao PackageConsumption i
/// CommissionEntry): unique (ParticipationId, SourceVersion = StatusVersion događaja), nikad se ne briše ni prepisuje;
/// status Active → Reversed (korekcija statusa) ili Active → Waived (otpis, nepovratno). Samo Active doprinosi dugu
/// (vidi ParticipationSettlement). PackageUnitConsumed/ClientPackageId su snapshot; trenutno pokriće se uvijek izvodi iz
/// aktivne povezane potrošnje paketa. Nema provizije ni veze na proviziju (D8).
/// Pisanje isključivo kroz IParticipationPolicyService; čitanje kroz Utils.PolicyConsequences.
/// </summary>
[Table("participation_policy_consequences")]
public class ParticipationPolicyConsequence
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("booking_segment_participation_id")]
    public Guid BookingSegmentParticipationId { get; set; }

    [Column("source_version")]
    public int SourceVersion { get; set; }

    [Column("event")]
    public PolicyConsequenceEvent Event { get; set; }

    [Column("fee_type")]
    public CancellationFeeType FeeType { get; set; }

    [Column("configured_fee_value")]
    public decimal? ConfiguredFeeValue { get; set; }

    [Column("fee_base_amount")]
    public decimal FeeBaseAmount { get; set; }

    [Column("calculated_fee_amount")]
    public decimal CalculatedFeeAmount { get; set; }

    [Column("was_fee_capped")]
    public bool WasFeeCapped { get; set; }

    [Column("cancellation_policy_id")]
    public Guid CancellationPolicyId { get; set; }

    [Column("cancellation_policy_version")]
    public int CancellationPolicyVersion { get; set; }

    [Column("package_action")]
    public CancellationPackageAction PackageAction { get; set; }

    [Column("package_unit_consumed")]
    public bool PackageUnitConsumed { get; set; }

    [Column("client_package_id")]
    public Guid? ClientPackageId { get; set; }

    [Column("status")]
    public PolicyConsequenceStatus Status { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("reversed_at")]
    public DateTimeOffset? ReversedAt { get; set; }

    [Column("reversed_by")]
    public Guid? ReversedBy { get; set; }

    [Column("reversal_reason")]
    public string ReversalReason { get; set; }

    [Column("waived_at")]
    public DateTimeOffset? WaivedAt { get; set; }

    [Column("waived_by")]
    public Guid? WaivedBy { get; set; }

    [Column("waiver_reason")]
    public string WaiverReason { get; set; }

    /// <summary>P2 (Q31) — snapshot dimenzije politike za sesiju pokrivenu članarinom u trenutku događaja (null = sesija nije
    /// bila pokrivena).</summary>
    [Column("membership_action")]
    public CancellationMembershipAction? MembershipAction { get; set; }

    [Column("client_membership_id")]
    public Guid? ClientMembershipId { get; set; }

    /// <summary>Claim zadržan uz ovu posljedicu (ForfeitCredit) — otpis ili reverzija posljedice ga vraća.</summary>
    [Column("membership_usage_id")]
    public Guid? MembershipUsageId { get; set; }

    /// <summary>Q26 — kredit perioda propada UMJESTO naknade (dug posljedice je 0).</summary>
    [Column("membership_credit_forfeited")]
    public bool MembershipCreditForfeited { get; set; }

    public BookingSegmentParticipation Participation { get; set; }
}
