using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;

/// <summary>
/// P2 (faza 2D, Q18/Q53) — član grupe čija je članarina u dugu uz postavku "blokiraj rezervaciju" preskočen je u budućem
/// segmentu generiranog occurrencea (ostaje član grupe). Popis za recepciju; kad dug prestane, naknadno dodavanje redom po
/// datumu dok ima mjesta, uz zapis razrješenja.
/// </summary>
[Table("group_occurrence_membership_skips")]
public class GroupOccurrenceMembershipSkip
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("group_id")]
    public Guid GroupId { get; set; }

    [Column("appointment_id")]
    public Guid AppointmentId { get; set; }

    [Column("appointment_segment_id")]
    public Guid AppointmentSegmentId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("skipped_at")]
    public DateTimeOffset SkippedAt { get; set; }

    [Column("skipped_by")]
    public Guid? SkippedBy { get; set; }

    [Column("resolution")]
    public GroupMembershipSkipResolution? Resolution { get; set; }

    [Column("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; set; }

    [Column("participation_id")]
    public Guid? ParticipationId { get; set; }
}
