using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Jedan fond godišnjeg odmora po zaposleniku po obračunskoj godini (FundYear) — otvara se lijeno (vidi
/// LeaveFundHandler.GetOrCreateForYear), bez scheduled joba. AllocatedDays je SNAPSHOT
/// EmployeeLeaveSettings.AnnualDays u trenutku otvaranja — kasnija promjena postavki ne mijenja retroaktivno
/// već otvorene fondove. UsedDays mutira LeaveFundAllocator pri upisu/brisanju "Godišnji" RosterEntry zapisa
/// (vidi RosterEntryService), uz xmin optimistic-concurrency token (isti obrazac kao ClientPackage.Version).
/// </summary>
[Table("leave_funds")]
public class LeaveFund
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("fund_year")]
    public int FundYear { get; set; }

    [Column("opened_at")]
    public DateTimeOffset OpenedAt { get; set; }

    /// <summary>Nakon ovog datuma neiskorišteni ostatak ovog fonda više nije prihvatljiv za trošenje (vidi LeaveFundHandler.GetEligible).</summary>
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("allocated_days")]
    public int AllocatedDays { get; set; }

    [Column("used_days")]
    public int UsedDays { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    /// <summary>Postgres sistemska xmin kolona kao optimistic-concurrency token (konfigurirano u DatabaseContext) — vidi ClientPackage.Version.</summary>
    [Column("xmin")]
    public uint Version { get; set; }

    public Employee Employee { get; set; }
}
