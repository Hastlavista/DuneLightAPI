using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Postavke fonda godišnjeg odmora po zaposleniku — koliko dana godišnje, kad se fond obnavlja i kad
/// istječe prijenos neiskorištenog ostatka iz prethodne godine. Zaseban entitet (ne polja na Employee),
/// isti obrazac kao WorkingHoursTemplate — modularno, opcionalno (ne mora svaki zaposlenik imati fond).
/// RenewalMonth/Day i CarryoverExpiryMonth/Day su mjesec+dan BEZ godine (ponavljaju se svake godine) —
/// vidi LeaveFundYearCalculator za izračun konkretnih datuma po obračunskoj godini.
/// </summary>
[Table("employee_leave_settings")]
public class EmployeeLeaveSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("annual_days")]
    public int AnnualDays { get; set; }

    [Column("renewal_month")]
    public int RenewalMonth { get; set; }

    [Column("renewal_day")]
    public int RenewalDay { get; set; }

    [Column("carryover_expiry_month")]
    public int CarryoverExpiryMonth { get; set; }

    [Column("carryover_expiry_day")]
    public int CarryoverExpiryDay { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Employee Employee { get; set; }
}
