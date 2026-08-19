using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Jedan interval radnog vremena unutar predloška (obrazac kao GroupSlot, prošireno s EndTime).
/// CycleWeekIndex je 0-based i generalizira "tjedan A/B": za Weekly uvijek 0, za Fortnightly 0/1,
/// za FourWeekly 0-3. Više intervala po (CycleWeekIndex, DayOfWeek) = pauze/dvokratni rad.
/// </summary>
[Table("working_hours_intervals")]
public class WorkingHoursInterval
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("working_hours_template_id")]
    public Guid WorkingHoursTemplateId { get; set; }

    [Column("cycle_week_index")]
    public int CycleWeekIndex { get; set; }

    [Column("day_of_week")]
    public DayOfWeek DayOfWeek { get; set; }

    [Column("start_time")]
    public TimeSpan StartTime { get; set; }

    [Column("end_time")]
    public TimeSpan EndTime { get; set; }

    public WorkingHoursTemplate WorkingHoursTemplate { get; set; }
}