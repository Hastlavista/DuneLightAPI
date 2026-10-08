using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>P2 (2E) — pravilo cjenovne pogodnosti verzije plana: opseg (AllServices | Service), tip (PercentOff | AmountOff |
/// FixedPrice) i vrijednost. Najviše jedno pravilo po usluzi i jedno AllServices po verziji (unique indeksi); nepromjenjivo kao i
/// verzija.</summary>
[Table("membership_plan_price_benefits")]
public class MembershipPlanPriceBenefit
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("membership_plan_version_id")]
    public Guid MembershipPlanVersionId { get; set; }

    [Column("scope")]
    public MembershipPriceBenefitScope Scope { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("benefit_type")]
    public MembershipPriceBenefitType Type { get; set; }

    [Column("value")]
    public decimal Value { get; set; }

    public MembershipPlanVersion PlanVersion { get; set; }
    public Service Service { get; set; }
}
