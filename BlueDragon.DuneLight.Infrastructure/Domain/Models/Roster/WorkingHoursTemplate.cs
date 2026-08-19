using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Planirani tjedni/ciklus predložak radnog vremena — vlasnik je točno jedno od EmployeeId/CompanyId
/// (isti obrazac kao PriceListItem.ServiceId/PackageId). Singleton po vlasniku ("vrijedi dok se ne
/// promijeni") — CRUD je upsert-u-mjestu, nema povijesti verzija. AnchorDate se normalizira na
/// ponedjeljak pri spremanju (vidi WorkingHoursTemplateService) radi predvidljivosti ciklusa.
/// </summary>
[Table("working_hours_templates")]
public class WorkingHoursTemplate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid? EmployeeId { get; set; }

    [Column("company_id")]
    public Guid? CompanyId { get; set; }

    [Column("cycle_type")]
    public WorkingHoursCycleType CycleType { get; set; }

    /// <summary>Referentni datum (ponedjeljak) od kojeg se broji koji je tjedan ciklusa (0-based) — vidi WorkingHoursCalculator.</summary>
    [Column("anchor_date")]
    public DateTimeOffset AnchorDate { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Employee Employee { get; set; }
    public Company Company { get; set; }
    public List<WorkingHoursInterval> Intervals { get; set; } = new();
}