using System;
using BlueDragon.DuneLight.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

/// <summary>
/// Poslovne postavke organizacije (1:1 s Organization) — odvojeno od OrganizationBranding (vizualni
/// identitet) jer je ovo poslovna konfiguracija, ne vizualna. Redak je OPCIONALAN po Organization: ako ne
/// postoji, primjenjuje se platformski default (vidi OrganizationSettingsService.DefaultPackageConsumptionTiming).
/// P1 (D1): rok otkazivanja više nije ovdje — prozor je dio verzije politike otkazivanja (CancellationPolicyVersion).
/// </summary>
[Table("organization_settings")]
public class OrganizationSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>Phase D3B3A — kad se paket troši (vidi PackageConsumptionTiming; trenutno samo OnCompletion). Kad redak
    /// ne postoji, vrijedi isti default (OrganizationSettingsService.DefaultPackageConsumptionTiming).</summary>
    [Column("package_consumption_timing")]
    public PackageConsumptionTiming PackageConsumptionTiming { get; set; } = PackageConsumptionTiming.OnCompletion;

    /// <summary>P2 (Q14) — rok najave u danima za nepovoljnu izmjenu plana prenesenu na postojeća članstva (default 30).</summary>
    [Column("membership_change_notice_days")]
    public int MembershipChangeNoticeDays { get; set; } = 30;

    /// <summary>P2 (Q15) — grace period u danima od dospijeća zaduženja (default 7).</summary>
    [Column("membership_grace_days")]
    public int MembershipGraceDays { get; set; } = 7;

    /// <summary>P2 (Q15) — ponašanje kod duga nakon grace perioda (default StopCovering).</summary>
    [Column("membership_debt_behavior")]
    public MembershipDebtBehavior MembershipDebtBehavior { get; set; } = MembershipDebtBehavior.StopCovering;

    /// <summary>P2 (2C) — automatski završi članstvo nakon N neplaćenih perioda; null = isključeno (default).</summary>
    [Column("membership_auto_end_after_unpaid_periods")]
    public int? MembershipAutoEndAfterUnpaidPeriods { get; set; }

    /// <summary>P2 (Q4) — ponašanje kad je limit članarine iskorišten (default FallbackToNextSource).</summary>
    [Column("membership_limit_exceeded_behavior")]
    public MembershipLimitExceededBehavior MembershipLimitExceededBehavior { get; set; } = MembershipLimitExceededBehavior.FallbackToNextSource;

    /// <summary>P2 (2F, Vagaro "Subtract Discounts") — osnovica provizije za odrađeno je cijena nakon popusta (prilagodbe cijene
    /// koje nisu članarinske; ručni iznos nije popust). Default false.</summary>
    [Column("commission_deduct_discounts")]
    public bool CommissionDeductDiscounts { get; set; }

    /// <summary>P2 (2F, Vagaro "Subtract Membership Discounts") — osnovica je cijena nakon popusta za članove; sesija koju članarina
    /// pokriva u cijelosti ima proviziju za odrađeno 0. Default false (osnovica = cijena sesije).</summary>
    [Column("commission_deduct_membership_discounts")]
    public bool CommissionDeductMembershipDiscounts { get; set; }

    /// <summary>P2 (2F, Q38) — provizija za kasni otkaz / izostanak (default Never).</summary>
    [Column("commission_late_cancellation")]
    public CommissionLateCancellationMode CommissionLateCancellation { get; set; } = CommissionLateCancellationMode.Never;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }
}
