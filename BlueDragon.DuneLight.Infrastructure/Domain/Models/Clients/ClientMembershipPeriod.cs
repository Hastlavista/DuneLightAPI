using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// P2 (faza 2C) — OTVOREN period članstva (redak nastaje kad period počne: pri prodaji za prvi period, inače u obnovi).
/// Granice se računaju isključivo kroz Utils.MembershipPeriodCalendar (ADR-0026); redak ih materijalizira zajedno s uvjetima
/// (verzija plana i cijena) koji su vrijedili za taj period. EndsOn tekućeg perioda obnova usklađuje s izračunom (pauza
/// produljuje period); završeni periodi se ne mijenjaju. Preskočeni (pauzirani) kalendarski periodi nemaju redak.
/// Unique (članstvo, početak).
/// </summary>
[Table("membership_periods")]
public class ClientMembershipPeriod
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("starts_on")]
    public DateOnly StartsOn { get; set; }

    [Column("ends_on")]
    public DateOnly EndsOn { get; set; }

    [Column("plan_version_id")]
    public Guid PlanVersionId { get; set; }

    /// <summary>Cijena perioda iz uvjeta (snapshot); nikad se ne preračunava.</summary>
    [Column("price")]
    public decimal Price { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public ClientMembership Membership { get; set; }
    public MembershipPlanVersion PlanVersion { get; set; }
}
