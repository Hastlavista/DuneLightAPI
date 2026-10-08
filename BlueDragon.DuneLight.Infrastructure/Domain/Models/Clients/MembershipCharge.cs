using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// P2 (faza 2C) — zaduženje (potraživanje) članarine: jedno po otvorenom periodu (Kind = Period) i početna naknada pri prodaji
/// (Kind = StartFee). Iznos je nepromjenjiv snapshot (kasnije se na njega veže račun / fiskalni zapis preko naplate).
/// SPREMLJEN je samo lifecycle (Open | WrittenOff | Voided); plaćenost se IZVODI iz aktivnih alokacija plaćanja preko stavki
/// checkouta tipa MembershipCharge (Q16). SettledAmount/SettlementStatus su PROJEKCIJA za brze upite (Q16.3), koju piše
/// isključivo MembershipChargeSettlement.Refresh u istoj transakciji kao promjenu alokacija; odluke unutar transakcije se uvijek
/// donose iz alokacija. Konačno zaduženje = Paid ili WrittenOff; PartiallyPaid je za pravila duga neplaćeno.
/// </summary>
[Table("membership_charges")]
public class MembershipCharge
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("client_membership_id")]
    public Guid ClientMembershipId { get; set; }

    [Column("period_id")]
    public Guid? PeriodId { get; set; }

    [Column("kind")]
    public MembershipChargeKind Kind { get; set; }

    [Column("description")]
    public string Description { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    /// <summary>Dospijeće: početak perioda (Q45.1: za prvi period datum početka članstva, ne datum prodaje).</summary>
    [Column("due_on")]
    public DateOnly DueOn { get; set; }

    [Column("lifecycle")]
    public MembershipChargeLifecycle Lifecycle { get; set; }

    [Column("settled_amount")]
    public decimal SettledAmount { get; set; }

    [Column("settlement_status")]
    public MembershipChargeSettlementStatus SettlementStatus { get; set; }

    [Column("written_off_at")]
    public DateTimeOffset? WrittenOffAt { get; set; }

    [Column("written_off_by")]
    public Guid? WrittenOffBy { get; set; }

    [Column("write_off_reason")]
    public string WriteOffReason { get; set; }

    [Column("voided_at")]
    public DateTimeOffset? VoidedAt { get; set; }

    [Column("voided_by")]
    public Guid? VoidedBy { get; set; }

    [Column("void_reason")]
    public string VoidReason { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    public ClientMembership Membership { get; set; }
    public ClientMembershipPeriod Period { get; set; }
    public List<CheckoutItem> CheckoutItems { get; set; } = new();
}
