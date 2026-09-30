using System;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>Zaposlenik dodijeljen segmentu termina. Složeni ključ (segment, zaposlenik) — isti zaposlenik najviše jednom
/// po segmentu. Namjerno bez uloge (primarni/sekundarni, vlasnik provizije/cijene) — te odluke su otvorene. Segment smije
/// imati nula zaposlenika (grupe bez trenera).</summary>
[Table("appointment_segment_employees")]
public class AppointmentSegmentEmployee
{
    [Column("appointment_segment_id")]
    public Guid AppointmentSegmentId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    public AppointmentSegment Segment { get; set; }
    public Employee Employee { get; set; }
}
