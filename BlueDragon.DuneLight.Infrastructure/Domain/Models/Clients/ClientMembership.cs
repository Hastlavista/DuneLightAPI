using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

/// <summary>
/// P2 (faza 2B, docs/p2) — članstvo klijenta u planu članarine. Članarina je zasebna domena, nije paket (ADR-0025).
/// Uvjeti: PlanVersionId pokazuje na NEPROMJENJIVU verziju plana — to je snapshot (Q14: izmjena plana nikad ne mijenja
/// uvjete retroaktivno). Zakazana promjena uvjeta od obnove (klijentova promjena plana ili izmjena plana prenesena na
/// postojeća članstva) je u Pending*; primjenjuje je obnova (2C).
/// Stanje (Scheduled/Active/Paused/Ended/Voided) se NE sprema — izvodi ga Utils.MembershipState iz datuma, pauza, EndsOn i
/// VoidedAt. EndsOn je zadnji dan članstva (uključivo) kad je završetak zakazan ili nastupio.
/// Granice perioda su determinističke (Utils.MembershipPeriodCalendar: StartsOn, AnchorDay, uvjeti, pauze); retci perioda i
/// zaduženja dolaze u 2C. Svaka naredba mijenja članstvo pod zaključanim retkom (FOR UPDATE) u jednoj transakciji.
/// </summary>
[Table("client_memberships")]
public class ClientMembership
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("membership_plan_id")]
    public Guid MembershipPlanId { get; set; }

    /// <summary>Trenutni uvjeti (nepromjenjiva verzija plana).</summary>
    [Column("plan_version_id")]
    public Guid PlanVersionId { get; set; }

    [Column("starts_on")]
    public DateOnly StartsOn { get; set; }

    /// <summary>2C — od kad vrijede trenutni uvjeti; granice perioda trenutnih uvjeta računaju se od ovog datuma. Pri prodaji =
    /// StartsOn; promjena uvjeta na obnovi postavlja datum obnove, osim kad PurchaseDate s istim intervalom zadržava izvorno
    /// sidro (MembershipPeriodCalendar). Rolling 12 mjeseci pauza i dalje se računa od StartsOn.</summary>
    [Column("terms_anchor_on")]
    public DateOnly TermsAnchorOn { get; set; }

    /// <summary>Pregled 2C (#8) — od kad se broje periodi minimalne obveze trenutnih uvjeta (prodaja = StartsOn, svaka primjena
    /// novih uvjeta = datum promjene).</summary>
    [Column("commitment_from_on")]
    public DateOnly CommitmentFromOn { get; set; }

    /// <summary>Pregled 2C (#8) — dosadašnji kraj minimalne obveze iz ranijih uvjeta; kraj obveze je kasniji od ovoga i kraja
    /// obveze trenutnih uvjeta (promjenom plana se obveza ne izbjegava). Null = nema.</summary>
    [Column("commitment_floor_on")]
    public DateOnly? CommitmentFloorOn { get; set; }

    /// <summary>Q3 — dan sidra obnove "od datuma kupnje" (dan datuma početka, 1–31); obnova je min(sidro, dani u mjesecu),
    /// uvijek od izvornog sidra.</summary>
    [Column("anchor_day")]
    public int AnchorDay { get; set; }

    [Column("sold_company_id")]
    public Guid SoldCompanyId { get; set; }

    [Column("sold_via")]
    public MembershipSaleChannel SoldVia { get; set; }

    [Column("sold_by")]
    public Guid? SoldBy { get; set; }

    /// <summary>Q43/Q44, 2F — korisnik provizije na prodaju (prva prodaja), JEDINI izvor: predlaže ga naredba prodaje (default
    /// prodavač), mijenja se naredbom na članstvu ili kroz stavku zaduženja prve prodaje u otvorenom checkoutu dok provizija nije
    /// nastala (događaj u povijesti članstva); nakon nastanka samo korekcija provizije (Q50). Null = bez provizije na prodaju.</summary>
    [Column("sale_commission_employee_id")]
    public Guid? SaleCommissionEmployeeId { get; set; }

    /// <summary>2F (Q42) — trenutak kad su zaduženje prvog perioda i početna naknada PRVI PUT bili konačni (provjera na Checkout
    /// Complete i otpisu) i provizija na prvu prodaju evaluirana; evaluira se jednom (i kad provizija ne nastane: nema korisnika,
    /// nema pravila ili je osnovica 0).</summary>
    [Column("first_sale_settled_at")]
    public DateTimeOffset? FirstSaleSettledAt { get; set; }

    /// <summary>2F — ishod te evaluacije; NoRecipient dopušta naknadnu dodjelu korisnika (commissions.manage + razlog).</summary>
    [Column("first_sale_commission_outcome")]
    public MembershipFirstSaleCommissionOutcome? FirstSaleCommissionOutcome { get; set; }

    /// <summary>2F — osnovica u trenutku evaluacije (stvarno plaćeno na zaduženjima prve prodaje); koristi je naknadna dodjela.</summary>
    [Column("first_sale_base_amount")]
    public decimal? FirstSaleBaseAmount { get; set; }

    [Column("pending_plan_version_id")]
    public Guid? PendingPlanVersionId { get; set; }

    [Column("pending_effective_on")]
    public DateOnly? PendingEffectiveOn { get; set; }

    [Column("pending_source")]
    public MembershipPendingChangeSource? PendingSource { get; set; }

    /// <summary>Pregled 2B — zakazana izmjena plana (PlanUpdate) koju je istisnula klijentova promjena plana, s IZVORNIM
    /// datumom (rok najave od objave verzije). Povlačenje klijentove promjene je vraća; izmjena objavljena dok klijentova
    /// promjena čeka također se pamti ovdje.</summary>
    [Column("displaced_plan_version_id")]
    public Guid? DisplacedPlanVersionId { get; set; }

    [Column("displaced_effective_on")]
    public DateOnly? DisplacedEffectiveOn { get; set; }

    /// <summary>Pregled 2B — TRAJNA oznaka: izmjena plana (verzija) nije primijenjena na ovo članstvo i zašto (npr.
    /// MEMBERSHIP_OVERLAPPING_COVERAGE), dok je kasnija uspješna izmjena ne riješi. Nema automatske ponovne primjene.</summary>
    [Column("plan_update_skipped_version_id")]
    public Guid? PlanUpdateSkippedVersionId { get; set; }

    [Column("plan_update_skipped_reason")]
    public string PlanUpdateSkippedReason { get; set; }

    [Column("plan_update_skipped_at")]
    public DateTimeOffset? PlanUpdateSkippedAt { get; set; }

    [Column("cancellation_requested_at")]
    public DateTimeOffset? CancellationRequestedAt { get; set; }

    [Column("cancellation_requested_by")]
    public Guid? CancellationRequestedBy { get; set; }

    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

    [Column("ends_on")]
    public DateOnly? EndsOn { get; set; }

    [Column("end_reason")]
    public MembershipEndReason? EndReason { get; set; }

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

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("updated_by")]
    public Guid? UpdatedBy { get; set; }

    public Client Client { get; set; }
    public MembershipPlan Plan { get; set; }
    public MembershipPlanVersion PlanVersion { get; set; }
    public MembershipPlanVersion PendingPlanVersion { get; set; }
    public MembershipPlanVersion DisplacedPlanVersion { get; set; }
    public MembershipPlanVersion PlanUpdateSkippedVersion { get; set; }
    public List<MembershipPause> Pauses { get; set; } = new();

    /// <summary>2C — otvoreni periodi (retci) i zaduženja.</summary>
    public List<ClientMembershipPeriod> Periods { get; set; } = new();
    public List<MembershipCharge> Charges { get; set; } = new();
}
