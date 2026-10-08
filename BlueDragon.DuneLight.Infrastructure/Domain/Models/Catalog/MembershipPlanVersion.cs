using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// P2 (faza 2A) — NEPROMJENJIVA verzija uvjeta plana članarine: sve što klijent osjeti (cijena, početna naknada, interval,
/// način obnove, opseg poslovnica, pokrivene usluge, limiti, minimalna obveza, otkazni rok, pravila pauze). Kreiranje verzije
/// je objava; verzija se nikad ne mijenja ni briše. Unique (plan, version). Članstvo kasnije čuva snapshot uvjeta svoje verzije
/// (Q14: izmjena plana nikad ne mijenja snapshot retroaktivno).
/// Pravila valjanosti su u Utils.MembershipPlanRules (Q3, Q5/Q12, Q13, Q17, Q29, Q49).
/// </summary>
[Table("membership_plan_versions")]
public class MembershipPlanVersion
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("membership_plan_id")]
    public Guid MembershipPlanId { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("price")]
    public decimal Price { get; set; }

    [Column("start_fee")]
    public decimal StartFee { get; set; }

    [Column("billing_interval")]
    public MembershipBillingInterval BillingInterval { get; set; }

    [Column("renewal_anchor")]
    public MembershipRenewalAnchor RenewalAnchor { get; set; }

    [Column("company_scope")]
    public MembershipCompanyScope CompanyScope { get; set; }

    /// <summary>Minimalno trajanje obveze u periodima koji nisu bili u pauzi; null = nema.</summary>
    [Column("minimum_commitment_periods")]
    public int? MinimumCommitmentPeriods { get; set; }

    /// <summary>Otkazni rok u danima prije obnove; null = nema.</summary>
    [Column("cancellation_notice_days")]
    public int? CancellationNoticeDays { get; set; }

    [Column("pause_allowed")]
    public bool PauseAllowed { get; set; }

    /// <summary>Samo PurchaseDate planovi (pauza po danima); null = bez ograničenja.</summary>
    [Column("max_pause_days")]
    public int? MaxPauseDays { get; set; }

    /// <summary>Samo CalendarMonth planovi (pauza u cijelim periodima); null = bez ograničenja.</summary>
    [Column("max_pause_periods")]
    public int? MaxPausePeriods { get; set; }

    /// <summary>Rolling 12 mjeseci od početka članstva; null = bez ograničenja.</summary>
    [Column("max_pauses_per_12_months")]
    public int? MaxPausesPer12Months { get; set; }

    /// <summary>Samo PurchaseDate planovi: pauza pomiče kraj perioda i obnovu za broj dana pauze.</summary>
    [Column("pause_extends_period")]
    public bool PauseExtendsPeriod { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public MembershipPlan Plan { get; set; }
    public List<MembershipPlanVersionService> Services { get; set; } = new();
    public List<MembershipPlanVersionCompany> Companies { get; set; } = new();
    public List<MembershipPlanUsageLimit> UsageLimits { get; set; } = new();

    /// <summary>P2 (2E) — pravila cjenovne pogodnosti za članove (nepokrivene sesije, Q2).</summary>
    public List<MembershipPlanPriceBenefit> PriceBenefits { get; set; } = new();
}
