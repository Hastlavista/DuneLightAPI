using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// Član grupe — "tko normalno dolazi". Ne mora se poklapati sa stvarnim dolaskom (vidi
/// AppointmentAttendance): netko tko nije član može doći na termin (zamjena, gost) bez da postane
/// član grupe. Soft-delete (IsActive) kod napuštanja grupe — čuva JoinedAt povijest; ponovni upis
/// nakon napuštanja stvara novi redak (vidi partial unique index na (GroupId, ClientId) WHERE IsActive).
/// </summary>
[Table("group_members")]
public class GroupMember
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("joined_at")]
    public DateTimeOffset JoinedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    public Group Group { get; set; }
    public Client Client { get; set; }
}
