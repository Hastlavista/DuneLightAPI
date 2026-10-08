using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

/// <summary>
/// P1 (ADR-0015, D1/D11) — imenovani profil politike otkazivanja. Pravila su u nepromjenjivim verzijama
/// (<see cref="CancellationPolicyVersion"/>); primjenjuje se uvijek najnovija verzija u trenutku događaja. Zadana politika
/// organizacije je profil s IsOrganizationDefault (točno jedan po organizaciji — djelomični unique indeks; nastaje pri
/// registraciji). Profil koji je zadani ili ima dodjelu ne može se deaktivirati; neaktivan se ne može dodijeliti ni
/// postaviti zadanim.
/// </summary>
[Table("cancellation_policies")]
public class CancellationPolicy
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("is_organization_default")]
    public bool IsOrganizationDefault { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public List<CancellationPolicyVersion> Versions { get; set; } = new();
}
