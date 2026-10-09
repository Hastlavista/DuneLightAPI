using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>T1-9 — povijest osjetljivih promjena na klijentu: tko, kada, staro/novo, razlog. Isti obrazac kao ostali audit
/// logovi modula (ClientMembershipAuditLog). Vrste promjena: <see cref="ClientAuditChangeTypes"/>. Zapis se upisuje u istom
/// contextu i istom SaveChanges kao i promjena koju opisuje.</summary>
[Table("client_audit_logs")]
public class ClientAuditLog
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

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

/// <summary>T1-9 — vrijednosti ClientAuditLog.ChangeType.</summary>
public static class ClientAuditChangeTypes
{
    /// <summary>Promjena zastavice i/ili datuma GDPR suglasnosti (kreiranje, izmjena, anonimizacija). Vrijednost:
    /// "given=true;date=2026-10-01" (date prazan kad ga nema).</summary>
    public const string GdprConsent = "GdprConsent";

    /// <summary>Ručni upis paketa s datumom kupnje prije današnjeg dana. NewValue: "clientPackageId=…;purchaseDate=…".</summary>
    public const string PackageIssuedBackdated = "PackageIssuedBackdated";

    public static string GdprValue(bool given, DateOnly? date) =>
        $"given={(given ? "true" : "false")};date={date?.ToString("yyyy-MM-dd") ?? ""}";
}
