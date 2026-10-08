using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>P2 — pokrivena usluga verzije plana (barem jedna u 2A). Usluga izvan liste nije pokrivena.</summary>
[Table("membership_plan_version_services")]
public class MembershipPlanVersionService
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("membership_plan_version_id")]
    public Guid MembershipPlanVersionId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    public MembershipPlanVersion PlanVersion { get; set; }
    public Service Service { get; set; }
}
