using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

/// <summary>Usluga koju zaposlenik smije izvoditi. Phase M1G (namjerna ispravka legacy ponašanja "prazno = nijedna"):
/// zaposlenik BEZ ijedne dodjele smije izvoditi SVE usluge; s jednom ili više dodjela — samo dodijeljene
/// (IEmployeeHandler.CanEmployeePerformService).</summary>
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
