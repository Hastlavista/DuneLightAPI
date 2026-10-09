using System;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>Resurs koji segment termina zauzima, u količini QuantityRequired (&gt; 0, CHECK u bazi). Složeni ključ
/// (segment, resurs) — isti resurs najviše jednom po segmentu. Invarijanta (ADR-0008, SchedulingConflictGuard): zbroj
/// QuantityRequired preklapajućih aktivnih segmenata &lt;= Resource.Capacity (tvrda blokada). K1-6: bez navedenih resursa
/// segment dobiva zadane resurse usluge (ServiceDefaultResource).</summary>
[Table("appointment_segment_resources")]
public class AppointmentSegmentResource
{
    [Column("appointment_segment_id")]
    public Guid AppointmentSegmentId { get; set; }

    [Column("resource_id")]
    public Guid ResourceId { get; set; }

    [Column("quantity_required")]
    public int QuantityRequired { get; set; }

    public AppointmentSegment Segment { get; set; }
    public Resource Resource { get; set; }
}
