using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Phase M1F — predložak jednog segmenta grupnog occurrencea. Vrijeme je RELATIVNO sidru occurrencea (lokalno vrijeme
/// slota u efektivnoj zoni poslovnice): lokalni početak segmenta = sidro + StartOffsetMinutes, lokalni kraj = početak +
/// DurationMinutes; razrješava se kroz postojeći kalendar poslovnice (zidni sat, DST) — predložak nikad ne nosi konkretno
/// vrijeme (ono je na generiranom AppointmentSegmentu). Capacity je MEKI poslovni broj mjesta TOG segmenta (prekoračenje
/// samo eksplicitnim zahtjevom uz groups.capacity.override); fizički kapacitet prostorije/resursa ostaje tvrd i odvojen.
/// Usluga.DefaultDuration je samo prijedlog pri kreiranju — predložak posjeduje svoje trajanje.
/// </summary>
[Table("group_segment_templates")]
public class GroupSegmentTemplate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("start_offset_minutes")]
    public int StartOffsetMinutes { get; set; }

    [Column("duration_minutes")]
    public int DurationMinutes { get; set; }

    [Column("room_id")]
    public Guid? RoomId { get; set; }

    [Column("capacity")]
    public int Capacity { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    public Group Group { get; set; }
    public Service Service { get; set; }
    public Room Room { get; set; }
    public List<GroupSegmentTemplateResource> Resources { get; set; } = new();
}

/// <summary>Phase M1F — generički resurs predloška (isti oblik kao AppointmentSegmentResource); kopira se u generirani segment.</summary>
[Table("group_segment_template_resources")]
public class GroupSegmentTemplateResource
{
    [Column("group_segment_template_id")]
    public Guid GroupSegmentTemplateId { get; set; }

    [Column("resource_id")]
    public Guid ResourceId { get; set; }

    [Column("quantity_required")]
    public int QuantityRequired { get; set; }

    public GroupSegmentTemplate Template { get; set; }
    public Resource Resource { get; set; }
}

/// <summary>Phase M1F — odabir predloška člana grupe. group_id je dio oba kompozitna FK-a (član i predložak iste grupe).</summary>
[Table("group_member_segment_templates")]
public class GroupMemberSegmentTemplate
{
    [Column("group_member_id")]
    public Guid GroupMemberId { get; set; }

    [Column("group_segment_template_id")]
    public Guid GroupSegmentTemplateId { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    public GroupMember Member { get; set; }
    public GroupSegmentTemplate Template { get; set; }
}
