using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// P1 (D1/D3/D4/D6/D11) — NEPROMJENJIVA objavljena verzija politike: prozor otkazivanja i pravila dvaju događaja
/// (kasno klijentsko otkazivanje, izostanak). Kreiranje verzije je objava; verzija se nikad ne mijenja ni briše.
/// Unique (policy, version). FeeValue: Fixed = iznos (&gt;= 0, 2 decimale), Percentage = 0–100 (2 decimale), None = NULL.
/// </summary>
[Table("cancellation_policy_versions")]
public class CancellationPolicyVersion
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("cancellation_policy_id")]
    public Guid CancellationPolicyId { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("cancellation_window_minutes")]
    public int CancellationWindowMinutes { get; set; }

    [Column("late_cancellation_fee_type")]
    public CancellationFeeType LateCancellationFeeType { get; set; }

    [Column("late_cancellation_fee_value")]
    public decimal? LateCancellationFeeValue { get; set; }

    [Column("late_cancellation_package_action")]
    public CancellationPackageAction LateCancellationPackageAction { get; set; }

    /// <summary>P2 (Q31) — kasni otkaz sesije pokrivene članarinom. Uvijek se postavlja eksplicitno pri nastanku verzije
    /// (CancellationPolicyRules.MembershipActionFor), nema defaulta u bazi.</summary>
    [Column("late_cancellation_membership_action")]
    public CancellationMembershipAction LateCancellationMembershipAction { get; set; }

    [Column("no_show_fee_type")]
    public CancellationFeeType NoShowFeeType { get; set; }

    [Column("no_show_fee_value")]
    public decimal? NoShowFeeValue { get; set; }

    [Column("no_show_package_action")]
    public CancellationPackageAction NoShowPackageAction { get; set; }

    /// <summary>P2 (Q31) — izostanak sa sesije pokrivene članarinom.</summary>
    [Column("no_show_membership_action")]
    public CancellationMembershipAction NoShowMembershipAction { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public CancellationPolicy Policy { get; set; }
}
