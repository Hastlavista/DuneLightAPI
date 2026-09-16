using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;

/// <summary>
/// Poslovne postavke organizacije (1:1 s Organization) — odvojeno od OrganizationBranding (vizualni
/// identitet) jer je ovo poslovna konfiguracija, ne vizualna. Redak je OPCIONALAN po Organization: ako ne
/// postoji, primjenjuje se platformski default (vidi OrganizationSettingsService.DefaultCancellationCutoffMinutes)
/// — postojeće organizacije ne trebaju ručnu migraciju podataka.
/// </summary>
[Table("organization_settings")]
public class OrganizationSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    /// <summary>Koliko minuta prije Appointment.StartsAt otkazivanje Bookinga prestaje biti "normalno" i
    /// postaje "kasno" — vidi BookingCancellationPolicy.IsLateCancellation. Mora biti &gt;= 0.</summary>
    [Column("cancellation_cutoff_minutes")]
    public int CancellationCutoffMinutes { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }
}
