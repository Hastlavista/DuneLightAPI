using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>P2 (2B) — povijest naredbi nad članstvom (prodaja, otkaz i povlačenje, pauze, promjena plana, raniji izlazak,
/// poništavanje, poništene zakazane pauze): tko, kada, staro/novo, razlog. Isti obrazac kao ostali audit logovi modula.</summary>
[Table("client_membership_audit_log")]
public class ClientMembershipAuditLog
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("change_type")]
    public string ChangeType { get; set; }

    [Column("old_value")]
    public string OldValue { get; set; }

    [Column("new_value")]
    public string NewValue { get; set; }

    [Column("reason")]
    public string Reason { get; set; }

    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    [Column("changed_by")]
    public Guid? ChangedBy { get; set; }
}
