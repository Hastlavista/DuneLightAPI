using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

/// <summary>
/// Audit log za roster — tko, kada, prethodna/nova vrijednost. ChangeType: "Created"/"Updated"/"Deleted".
/// Za razliku od EmployeeAuditLog/GroupAuditLog/AppointmentAuditLog (koji prate pojedina osjetljiva polja),
/// ovdje se OldValue/NewValue bilježe kao sažetak cijelog retka, jer se rad/odsutnost mijenja/briše kao cjelina
/// i brisanje retka (dopušteno u rosteru) mora ostati vidljivo i nakon što je zapis fizički obrisan — zato
/// namjerno NEMA FK na roster_entries (vidi migraciju).
/// </summary>
[Table("roster_audit_log")]
public class RosterAuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("roster_entry_id")]
    public Guid RosterEntryId { get; set; }

    [Column("change_type")]
    public string ChangeType { get; set; }

    [Column("old_value")]
    public string OldValue { get; set; }

    [Column("new_value")]
    public string NewValue { get; set; }

    [Column("changed_at")]
    public DateTimeOffset ChangedAt { get; set; }

    [Column("changed_by")]
    public Guid? ChangedBy { get; set; }
}
