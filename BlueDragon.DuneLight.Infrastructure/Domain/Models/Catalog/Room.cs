using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// Prostorija poslovnice (npr. "Masaža", "Vježbanje 1") — dodjeljuje se na segment termina (AppointmentSegment.RoomId) i/ili
/// Group.DefaultRoomId (isti obrazac kao DefaultTrainerId, snapshotira se na generirani termin).
/// </summary>
[Table("rooms")]
public class Room
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    /// <summary>Maksimalan broj OSOBA (dodijeljeni zaposlenici + zauzimajući klijenti segmenata koji rezerviraju slot) koje
    /// istovremeno smiju boraviti u prostoriji (≥ 1). Phase M1D: JEDINO, tvrdo pravilo prostorije u zakazivanju (vremenski
    /// raslojeno, bez override-a) — NIJE broj termina/segmenata/rezervacija. Vidi RoomPeopleCount/SchedulingConflictGuard.</summary>
    [Column("capacity")]
    public int Capacity { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("note")]
    public string Note { get; set; }

    [Column("sort_order")]
    public int SortOrder { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Company Company { get; set; }
}
