using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

/// <summary>Tvrtka na kojoj zaposlenik radi. Točno jedna po zaposleniku ima IsPrimary = true (matična).</summary>
[Table("employee_companies")]
public class EmployeeCompany
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    public Employee Employee { get; set; }
    public Company Company { get; set; }
}
