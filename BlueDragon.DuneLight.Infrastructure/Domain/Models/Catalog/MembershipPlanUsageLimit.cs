using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>P2 (Q13/Q17) — limit korištenja verzije plana. ServiceId = null je limit cijelog plana (sve pokrivene usluge
/// zajedno); Window = Period su krediti jednog perioda. Unique (verzija, usluga ili plan, prozor).</summary>
[Table("membership_plan_usage_limits")]
public class MembershipPlanUsageLimit
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("membership_plan_version_id")]
    public Guid MembershipPlanVersionId { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("usage_window")]
    public MembershipUsageWindow Window { get; set; }

    [Column("max_uses")]
    public int MaxUses { get; set; }

    public MembershipPlanVersion PlanVersion { get; set; }
    public Service Service { get; set; }
}
