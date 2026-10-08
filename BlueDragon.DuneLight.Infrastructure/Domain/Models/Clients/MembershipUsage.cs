using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// P2 (faza 2D) — unos ledgera korištenja članarine (pravilo pokrića 3): Claim (−1) kad članarina pokrije sudjelovanje,
/// Release (+1) s referencom na claim kad se kredit vrati. Nikad se ne briše; claim se pri stornu samo označava neaktivnim
/// (IsActive = false + ReleasedAt/ReleaseReason), a storno je zaseban redak. Aktivan claim je najviše jedan po sudjelovanju.
/// Brojači limita su izvedeni: broj AKTIVNIH claimova članstva u prozoru (ServiceDate, kalendar poslovnice termina, Q17) ili
/// periodu (PeriodDate, kalendar organizacije, Q22), za limit usluge samo claimovi te usluge (Q13).
/// ParticipationId namjerno nema FK (vidi migraciju 2D) — ostaje trajna referenca i nakon brisanja netaknutog sudjelovanja.
/// Piše isključivo IMembershipCoverageService pod lockom članstva.
/// </summary>
[Table("membership_usages")]
public class MembershipUsage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("participation_id")]
    public Guid ParticipationId { get; set; }

    [Column("service_id")]
    public Guid ServiceId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    /// <summary>Lokalni datum termina u kalendaru poslovnice (prozori limita).</summary>
    [Column("service_date")]
    public DateOnly ServiceDate { get; set; }

    /// <summary>Lokalni datum termina u kalendaru organizacije (pripadnost periodu).</summary>
    [Column("period_date")]
    public DateOnly PeriodDate { get; set; }

    [Column("entry_type")]
    public MembershipUsageEntryType EntryType { get; set; }

    [Column("units")]
    public short Units { get; set; }

    [Column("reverses_usage_id")]
    public Guid? ReversesUsageId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("released_at")]
    public DateTimeOffset? ReleasedAt { get; set; }

    [Column("release_reason")]
    public MembershipUsageReleaseReason? ReleaseReason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }
}
