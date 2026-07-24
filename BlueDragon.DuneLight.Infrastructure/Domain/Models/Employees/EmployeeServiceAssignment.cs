using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

/// <summary>Usluga koju zaposlenik smije izvoditi. Prazan popis za zaposlenika = smije sve usluge.</summary>
[Table("employee_services")]
public class EmployeeServiceAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    public Employee Employee { get; set; }
    public Service Service { get; set; }
}
