using System;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>Resurs koji segment termina zauzima, u količini QuantityRequired (&gt; 0, CHECK u bazi). Složeni ključ
/// (segment, resurs) — isti resurs najviše jednom po segmentu. Buduća invarijanta (NIJE implementirana): zbroj
/// QuantityRequired preklapajućih aktivnih segmenata &lt;= Resource.Capacity.</summary>
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
