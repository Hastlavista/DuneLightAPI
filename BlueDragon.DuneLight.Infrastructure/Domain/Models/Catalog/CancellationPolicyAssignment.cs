using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// P1 (D1) — dodjela profila politike po scopeu: Company+Service, samo Service ili samo Company (barem jedno, DB CHECK).
/// Jedna dodjela po scopeu (tri djelomična unique indeksa). Pokazuje na PROFIL, nikad na verziju. Razrješavanje:
/// Company+Service → Service → Company → zadana politika organizacije.
/// </summary>
[Table("cancellation_policy_assignments")]
public class CancellationPolicyAssignment
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("company_id")]
    public Guid? CompanyId { get; set; }

    [Column("service_id")]
    public Guid? ServiceId { get; set; }

    [Column("cancellation_policy_id")]
    public Guid CancellationPolicyId { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public CancellationPolicy Policy { get; set; }
    public Company Company { get; set; }
    public Service Service { get; set; }
}
