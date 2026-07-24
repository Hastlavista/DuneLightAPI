using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

/// <summary>Lokacija na kojoj zaposlenik radi. Točno jedna po zaposleniku ima IsPrimary = true (matična).</summary>
[Table("employee_locations")]
public class EmployeeLocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("location_id")]
    public Guid LocationId { get; set; }

    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    public Employee Employee { get; set; }
    public Location Location { get; set; }
}
