using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>P2 (Q29) — odabrana poslovnica verzije plana; postoji samo uz CompanyScope = SelectedCompanies.</summary>
[Table("membership_plan_version_companies")]
public class MembershipPlanVersionCompany
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("membership_plan_version_id")]
    public Guid MembershipPlanVersionId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    public MembershipPlanVersion PlanVersion { get; set; }
    public Company Company { get; set; }
}
