using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Definicija grupe (npr. "Yoga pon-sri 19h"). Traje neograničeno (nema datuma kraja). Trener NIJE
/// dio identiteta grupe — DefaultTrainerId je samo prijedlog koji se snapshotira na generirani
/// termin i može se mijenjati po pojedinom terminu (zamjene) bez diranja grupe. Bez pravog brisanja
/// — samo deaktivacija (IsActive); deaktivacija ne dira već generirane termine, zaustavlja samo
/// buduće generiranje.
/// </summary>
[Table("groups")]
public class Group
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("location_id")]
    public Guid LocationId { get; set; }

    [Column("capacity")]
    public int Capacity { get; set; }

    /// <summary>Prijedlog trenera za generirane termine. Smije biti prazan (ručna dodjela po terminu).</summary>
    [Column("default_trainer_id")]
    public Guid? DefaultTrainerId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("note")]
    public string Note { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Service Service { get; set; }
    public Location Location { get; set; }
    public Employee DefaultTrainer { get; set; }
    public List<GroupSlot> Slots { get; set; } = new();
    public List<GroupMember> Members { get; set; } = new();
}
