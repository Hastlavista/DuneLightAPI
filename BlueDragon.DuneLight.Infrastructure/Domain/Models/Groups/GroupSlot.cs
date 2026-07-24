using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Tjedni slot grupe (dan u tjednu + vrijeme početka) — trajanje termina se uzima iz usluge grupe.
/// Soft-delete (IsActive) umjesto tvrdog brisanja jer generirani Appointment.GroupSlotId referencira
/// ovaj redak; izmjena/uklanjanje slota ne dira već generirane buduće termine, utječe samo na
/// sljedeća pokretanja generiranja.
/// </summary>
[Table("group_slots")]
public class GroupSlot
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("day_of_week")]
    public DayOfWeek DayOfWeek { get; set; }

    [Column("start_time")]
    public TimeSpan StartTime { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Group Group { get; set; }
}
