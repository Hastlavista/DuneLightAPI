using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

/// <summary>
/// Nepromjenjiv zapis staff-compensation ledgera — "Employee X je zaradio Y provizije jer Z pod pravilom-
/// snapshotom R" (vidi spec). BaseAmount/CalculationType/RuleValue/CommissionAmount su SNAPSHOT u trenutku
/// zarade — kasnija promjena CommissionRule ne mijenja retroaktivno ove retke (izvještaji čitaju OVE stupce,
/// nikad trenutni CommissionRule, vidi CommissionService).
///
/// Izvor je točno JEDAN od BookingId (IndividualService)/AppointmentId (GroupService, po CIJELOM terminu, ne po
/// sudioniku — vidi CommissionSourceType)/CheckoutItemId (ProductSale/PackageSale), prema SourceType. Idempotencija
/// je DB-garantirana (ne "if not exists"): unique indeks na (BookingId, SourceVersion) (nullable, nenul samo za
/// IndividualService — vidi Migration_2026_09_25), unique indeks na CheckoutItemId (nullable, nenul samo za
/// Product/PackageSale), i djelomični unique indeks na AppointmentId WHERE source_type='GroupService' (vidi
/// migraciju) — svaka poslovna pojava (COMPLETION-OCCURRENCE za IndividualService, ne samo booking) može
/// proizvesti najviše jedan CommissionEntry, čak i pod konkurentnim/ponovljenim zahtjevima.
///
/// Phase M0: za IndividualService izvor je SUDJELOVANJE (BookingSegmentParticipationId), unique indeks je na
/// (BookingSegmentParticipationId, SourceVersion); BookingId ostaje kontekst.
///
/// Phase M1G (više zaposlenika): KORISNIK je dio identiteta — individualna provizija je jedinstvena po (sudjelovanje,
/// zaposlenik, SourceVersion): svaki zaposlenik segmenta zarađuje SVOJ zapis neovisno (bez dijeljenja, bez "glavnog");
/// grupna provizija je jedinstvena po (AppointmentSegmentId, zaposlenik) — izvor grupne sesije je KONKRETNI SEGMENT
/// occurrencea (ne termin, ne prvi segment), jednom po zaposleniku segmenta.
///
/// SourceVersion (samo za SourceType=IndividualService, inače uvijek 0) je StatusVersion sudjelovanja snapshotan u
/// TRENUTKU zarade — daje stabilan identitet JEDNOJ konkretnoj completion-pojavi istog Bookinga (isti obrazac kao
/// Booking.StatusVersion/Notification.SourceVersion), jer se Individual Booking legitimno može vratiti na
/// Confirmed nakon Completed (BookingService.ApplyIndividualCompletionCorrection — poništenje pogrešnog
/// check-ina) i kasnije ponovno odraditi, što mora zaraditi NOVI Earned zapis bez sudara sa starim
/// (sad Reversed) zapisom iste geneze.
///
/// Status=Earned je jedina vrijednost koju je ovaj MVP prvotno proizvodio (vidi CommissionEntryStatus) —
/// individualni Booking-completion sada IMA reverzijsku putanju (ApplyIndividualCompletionCorrection, vidi gore),
/// grupni Appointment-completion i dalje NEMA (vidi CommissionService domensku napomenu za punu analizu zašto).
/// ReversedAt/ReversedBy (vidi ispod) bilježe tko/kada je zapis reverziran — isti obrazac kao
/// checkouts.cancelled_at/cancelled_by. Retke se NIKAD ne briše niti cascade-briše kad se
/// CommissionRule obriše (CommissionRuleId je Restrict FK, referencirano pravilo se ne može trajno obrisati —
/// vidi CommissionRuleHandler.IsReferenced) — jedina cascade veza je na Appointment/Booking (isto ponašanje kao
/// AppointmentAuditLog: "isti dan" hard-delete termina briše i njegov povijesni trag, vidi
/// AppointmentService.Delete/DatabaseContext).
/// </summary>
[Table("commission_entries")]
public class CommissionEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("employee_id")]
    public Guid EmployeeId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("commission_rule_id")]
    public Guid CommissionRuleId { get; set; }

    [Column("source_type")]
    public CommissionSourceType SourceType { get; set; }

    [Column("appointment_id")]
    public Guid? AppointmentId { get; set; }

    [Column("booking_id")]
    public Guid? BookingId { get; set; }

    /// <summary>Phase M0: IZVOR IndividualService provizije je SUDJELOVANJE (izvršna jedinica) — idempotencija i reverzija
    /// po (BookingSegmentParticipationId, SourceVersion), gdje je SourceVersion StatusVersion OVOG sudjelovanja.
    /// BookingId/AppointmentId ostaju kontekst (spremnik/termin) za izvještaje; složeni FK jamči da sudjelovanje pripada
    /// tom Bookingu. Null za ostale izvore.</summary>
    [Column("booking_segment_participation_id")]
    public Guid? BookingSegmentParticipationId { get; set; }

    [Column("checkout_item_id")]
    public Guid? CheckoutItemId { get; set; }

    /// <summary>Phase M1G — izvor GroupService provizije: segment occurrencea (usluga + zaposlenici sesije). Null za ostale
    /// izvore.</summary>
    [Column("appointment_segment_id")]
    public Guid? AppointmentSegmentId { get; set; }

    /// <summary>P2 (2F, Q42) — izvor MembershipSale provizije: članstvo (prva prodaja). Null za ostale izvore.</summary>
    [Column("client_membership_id")]
    public Guid? ClientMembershipId { get; set; }

    /// <summary>P2 (2F, Q38) — izvor PolicyFee provizije: posljedica politike čija je naknada plaćena (uz sudjelovanje, Booking i
    /// termin kao kontekst). Null za ostale izvore.</summary>
    [Column("participation_policy_consequence_id")]
    public Guid? ParticipationPolicyConsequenceId { get; set; }

    /// <summary>OSNOVICA primijenjenog izračuna u trenutku zarade. IndividualService (P2 2F): cijena sesije (ručni iznos ili
    /// cjenik) umanjena prema primijenjenim postavkama osnovice; PolicyFee: plaćena naknada; Product/PackageSale:
    /// CheckoutItem.Amount; MembershipSale: stvarno plaćeno za prvi period + početnu naknadu. 0 za GroupService (samo Fixed).</summary>
    [Column("base_amount")]
    public decimal BaseAmount { get; set; }

    /// <summary>Primijenjeni izračun (osnovno pravilo ili nadjačavanje po načinu plaćanja, vidi OverrideApplied).</summary>
    [Column("calculation_type")]
    public CommissionCalculationType CalculationType { get; set; }

    [Column("rule_value")]
    public decimal RuleValue { get; set; }

    [Column("commission_amount")]
    public decimal CommissionAmount { get; set; }

    /// <summary>P2 (2F, Q28.6) — način plaćanja sesije u trenutku zarade (IndividualService, PolicyFee); null za ostale izvore.</summary>
    [Column("payment_source")]
    public CommissionPaymentSource? PaymentSource { get; set; }

    /// <summary>P2 (2F) — izvor pokrića: članstvo (Membership) ili paket klijenta (Package); null kod izravne naplate.</summary>
    [Column("coverage_source_id")]
    public Guid? CoverageSourceId { get; set; }

    /// <summary>P2 (2F) — cijena sesije prije oduzimanja popusta i pokrića: ručni iznos ako je upisan, inače cjenik.</summary>
    [Column("session_price_amount")]
    public decimal? SessionPriceAmount { get; set; }

    /// <summary>P2 (2F) — cijena iz cjenika (za izvještaje o ručnim sniženjima); null kad cijena nije razriješena iz cjenika.</summary>
    [Column("list_price_amount")]
    public decimal? ListPriceAmount { get; set; }

    [Column("is_manual_price")]
    public bool? IsManualPrice { get; set; }

    /// <summary>P2 (2F, Vagaro) — primijenjene postavke osnovice organizacije (snapshot; kasnija promjena postavki ne mijenja
    /// zarađenu proviziju): "oduzmi popuste" i "oduzmi popuste članstva". Null za izvore na koje se postavke ne odnose.</summary>
    [Column("deduct_discounts")]
    public bool? DeductDiscounts { get; set; }

    [Column("deduct_membership_discounts")]
    public bool? DeductMembershipDiscounts { get; set; }

    /// <summary>P2 (2F, Vagaro) — razina primijenjenog pravila: pravilo predmeta ili opće pravilo zaposlenika (AllServices).</summary>
    [Column("applied_rule_scope")]
    public CommissionRuleScope? AppliedRuleScope { get; set; }

    /// <summary>P2 (2F, Vagaro) — objašnjenje izbora pravila (jsonb): primijenjeno pravilo i zašto, te neprimijenjena pravila s
    /// razlogom (npr. "pravilo za uslugu ima prednost pred općim"). Snapshot u trenutku nastanka.</summary>
    [Column("rule_evaluation")]
    public string RuleEvaluation { get; set; }

    /// <summary>P2 (2F, Q38) — Fixed iznos je ograničen na iznos naknade.</summary>
    [Column("was_capped")]
    public bool WasCapped { get; set; }

    [Column("status")]
    public CommissionEntryStatus Status { get; set; }

    /// <summary>StatusVersion izvornog SUDJELOVANJA u trenutku zarade (SourceType=IndividualService) — vidi klasnu napomenu. Dio
    /// unique indeksa uz BookingSegmentParticipationId (Phase M0). P2 (2F): za ProductSale/PackageSale/MembershipSale/PolicyFee je
    /// redni broj pojave izvora (0 prva, korekcija Q50 ili ponovno plaćena naknada +1), dio unique indeksa izvora. GroupService 0.</summary>
    [Column("source_version")]
    public int SourceVersion { get; set; }

    /// <summary>Poslovni trenutak zarade (kad je izvor stvarno odrađen/prodan) — koristi se za date-range upite,
    /// ne nužno identično CreatedAt (iako u ovom MVP-u uvijek jest, jer se zapis stvara unutar iste transakcije
    /// kao prijelaz koji ga zarađuje).</summary>
    [Column("earned_at")]
    public DateTimeOffset EarnedAt { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Popunjeno SAMO kad je Status=Reversed — vidi ApplyIndividualCompletionCorrection.</summary>
    [Column("reversed_at")]
    public DateTimeOffset? ReversedAt { get; set; }

    [Column("reversed_by")]
    public Guid? ReversedBy { get; set; }

    /// <summary>P2 (2F) — razlog storna (npr. korekcija korisnika provizije, Q50 — obavezan razlog; storno/oprost naknade).</summary>
    [Column("reversal_reason")]
    public string ReversalReason { get; set; }

    /// <summary>P2 (2F, Q50) — ova provizija je nastala korekcijom korisnika provizije na prodaju zapisa s ovim Id-em.</summary>
    [Column("correction_of_entry_id")]
    public Guid? CorrectionOfEntryId { get; set; }

    public Employee Employee { get; set; }
    public Company Company { get; set; }
    public CommissionRule CommissionRule { get; set; }
    public Appointment Appointment { get; set; }
    public Booking Booking { get; set; }
    public CheckoutItem CheckoutItem { get; set; }
}
