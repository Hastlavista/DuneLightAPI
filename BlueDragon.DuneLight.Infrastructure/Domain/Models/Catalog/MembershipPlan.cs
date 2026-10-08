using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// P2 (faza 2A) — plan članarine (katalog organizacije). Članarina je zasebna domena, nije podtip paketa (Target Arch §15).
/// Profil nosi samo ono što klijent ne "osjeti" (naziv, opis, aktivnost, kapacitet prodaje); uvjeti su u nepromjenjivim
/// verzijama (<see cref="MembershipPlanVersion"/>), isti obrazac kao CancellationPolicy. Plan se ne briše, samo deaktivira:
/// neaktivan plan se ne prodaje, a postojeća članstva traju do kraja tekućeg perioda i ne obnavljaju se.
/// Aktivni naziv je jedinstven po organizaciji (trim + case-insensitive, djelomični unique indeks).
/// </summary>
[Table("membership_plans")]
public class MembershipPlan
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>Najveći broj aktivnih članstava plana (provjera pri prodaji, pod lockom plana); null = bez ograničenja.
    /// Zasebno od kapaciteta termina.</summary>
    [Column("max_active_memberships")]
    public int? MaxActiveMemberships { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public List<MembershipPlanVersion> Versions { get; set; } = new();
}
