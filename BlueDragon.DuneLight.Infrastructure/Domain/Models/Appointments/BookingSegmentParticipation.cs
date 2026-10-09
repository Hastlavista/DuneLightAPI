using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Klijent Bookinga sudjeluje u JEDNOM segmentu istog termina (ciljna izvršna i cjenovna jedinica). Od Phase D3B1 je
/// AUTORITATIVAN izvor izvršnog životnog ciklusa (Status, StatusVersion, dolazak, razlog/klasifikacija otkazivanja) —
/// svaki produkcijski Booking ima točno jedno sudjelovanje (BookingFactory), a čita/piše se kroz
/// Utils.ParticipationLifecycle (Phase M0: adresirano sudjelovanjem). Od Phase D3B2 je AUTORITATIVAN i za cijenu (Amount, SuggestedAmount,
/// IsAmountManuallyOverridden — NOT NULL; Booking više nema cijenu), čita se kroz BookingParticipations.AmountOf/...,
/// mijenja kroz BookingFactory (nastanak) i Utils.ParticipationPrice (re-cijenjenje). Od Phase D3B3A je i nositelj povijesti
/// potrošnje paketa (PackageConsumptions), a od Phase D3B3B i GRANICA NOVČANOG NAMIRENJA (CheckoutItems -&gt;
/// PaymentAllocation; izračun Utils.ParticipationSettlement).
///
/// Invarijante (provodi ih jedina write-putanja, IBookingSegmentParticipationHandler.Add): Booking i segment postoje u
/// organizaciji sudjelovanja i pripadaju ISTOM terminu; (BookingId, AppointmentSegmentId) je jedinstven (i u bazi).
///
/// Status/StatusVersion: isti koncept kao Booking — StatusVersion raste samo kad se status stvarno promijeni. Novo
/// sudjelovanje počinje sa StatusVersion = 0 bez obzira na početni status (nastanak nije prijelaz); legacy Booking
/// ponašanje (F-07) se ne dira.
///
/// Cijena (bez nove logike prvenstva): BaseAmount + BaseAmountSource (postojeći PriceSource razrješavanja cjenika) →
/// SuggestedAmount (AdjustmentAmount = opcionalna razlika zbog prilagodbe; IZVOR prilagodbe namjerno nije modeliran —
/// prvenstvo članarina/oznaka/promocija je otvorena odluka) → Amount (konačna) + IsAmountManuallyOverridden.
/// BaseAmount/BaseAmountSource su popunjeni samo kad je cijena STVARNO razriješena iz cjenika (inače NULL — npr.
/// poništen grupni check-in); AdjustmentAmount se trenutno nikad ne piše (ne postoji eksplicitna prilagodba).
/// Nazivi prate Booking (Amount/SuggestedAmount/IsAmountManuallyOverridden). Paket/namirenje namjerno nisu ovdje.
///
/// Otkazivanje (P1, ADR-0016): strukturirani TRENUTNI metapodaci — initiator (Client/Business/System), CancelledAt/By,
/// razlog, a samo za Client i klasifikacija kasno/na vrijeme sa snapshotom politike (PolicyId/Version, primijenjeni
/// prozor); izostanak ima vlastite NoShowAt/By/Reason. Metapodaci uvijek odgovaraju statusu (DB CHECK); kasno otkazivanje je
/// klasifikacija uz Status=Cancelled, ne status, i računa se iz PlannedStart SEGMENTA ovog sudjelovanja (CancellationPolicyRules).
/// Posljedice politike su u ledgeru PolicyConsequences. Dolazak (ArrivedAt/ArrivedBy) je metapodatak, ne status.
/// </summary>
[Table("booking_segment_participations")]
public class BookingSegmentParticipation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("booking_id")]
    public Guid BookingId { get; set; }

    [Column("appointment_segment_id")]
    public Guid AppointmentSegmentId { get; set; }

    [Column("status")]
    public ParticipationStatus Status { get; set; }

    [Column("status_version")]
    public int StatusVersion { get; set; }

    [Column("arrived_at")]
    public DateTimeOffset? ArrivedAt { get; set; }

    [Column("arrived_by")]
    public Guid? ArrivedBy { get; set; }

    /// <summary>P1 (D2/D3) — TRENUTNI metapodaci otkazivanja; popunjeni samo dok je Status Cancelled (DB CHECK), inače
    /// NULL. Povijest je u audit logu i ledgeru posljedica. Piše isključivo Utils.ParticipationEventMetadata.</summary>
    [Column("cancellation_initiator")]
    public CancellationInitiator? CancellationInitiator { get; set; }

    [Column("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    [Column("cancelled_by")]
    public Guid? CancelledBy { get; set; }

    /// <summary>Opcionalan za Client, obavezan za Business, kod sustava za System.</summary>
    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

    /// <summary>Samo Client: kasno (true) / na vrijeme (false) prema prozoru primijenjene verzije politike; inače NULL.</summary>
    [Column("is_late_cancellation")]
    public bool? IsLateCancellation { get; set; }

    /// <summary>Samo Client: profil i verzija politike razriješeni u trenutku otkazivanja (snapshot).</summary>
    [Column("cancellation_policy_id")]
    public Guid? CancellationPolicyId { get; set; }

    [Column("cancellation_policy_version")]
    public int? CancellationPolicyVersion { get; set; }

    [Column("applied_cancellation_window_minutes")]
    public int? AppliedCancellationWindowMinutes { get; set; }

    /// <summary>P1 (D3) — TRENUTNI metapodaci izostanka; popunjeni samo dok je Status NoShow (NoShow nema initiator).</summary>
    [Column("no_show_at")]
    public DateTimeOffset? NoShowAt { get; set; }

    [Column("no_show_by")]
    public Guid? NoShowBy { get; set; }

    [Column("no_show_reason")]
    public string NoShowReason { get; set; }

    /// <summary>K1-4 — šifra razloga otkazivanja / izostanka i naziv u trenutku događaja (snapshot). Metapodaci odgovaraju
    /// statusu (DB CHECK): otkazivanje samo uz Cancelled, izostanak samo uz NoShow.</summary>
    [Column("cancellation_reason_code_id")]
    public Guid? CancellationReasonCodeId { get; set; }

    [Column("cancellation_reason_code_name")]
    public string CancellationReasonCodeName { get; set; }

    [Column("no_show_reason_code_id")]
    public Guid? NoShowReasonCodeId { get; set; }

    [Column("no_show_reason_code_name")]
    public string NoShowReasonCodeName { get; set; }

    [Column("base_amount")]
    public decimal? BaseAmount { get; set; }

    [Column("base_amount_source")]
    public PriceSource? BaseAmountSource { get; set; }

    /// <summary>Phase M1G — POVIJESNI izvor cijene korišten pri razrješavanju (Standard/Employee); null kad BaseAmount nije
    /// razriješen iz cjenika. Nikad se ne izvodi iz trenutnog stanja segmenta.</summary>
    [Column("pricing_mode")]
    public Core.Enums.SegmentPricingMode? PricingMode { get; set; }

    /// <summary>Zaposlenik čije su razine cjenika razmatrane pri razrješavanju (izvor Employee); inače null.</summary>
    [Column("pricing_employee_id")]
    public Guid? PricingEmployeeId { get; set; }

    [Column("adjustment_amount")]
    public decimal? AdjustmentAmount { get; set; }

    /// <summary>P2 (2E, Q1/§10.4) — primijenjena prilagodba (tip + izvor + snapshot pravila, jsonb) uz AdjustmentAmount; null =
    /// predložena cijena je cjenik. Piše samo IMembershipCoverageService (jedini izvor u P2 je članarina).</summary>
    [Column("adjustment_type")]
    public Core.Enums.PriceAdjustmentType? AdjustmentType { get; set; }

    [Column("adjustment_source_id")]
    public Guid? AdjustmentSourceId { get; set; }

    [Column("adjustment_rule_snapshot")]
    public string AdjustmentRuleSnapshot { get; set; }

    /// <summary>Evaluacija SVIH kandidata u trenutku cijene (jsonb: tip, izvor, cijena, ishod, razlog) — snapshot, nikad se ne
    /// računa naknadno.</summary>
    [Column("adjustment_evaluation")]
    public string AdjustmentEvaluation { get; set; }

    [Column("suggested_amount")]
    public decimal SuggestedAmount { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("is_amount_manually_overridden")]
    public bool IsAmountManuallyOverridden { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    public Booking Booking { get; set; }
    public AppointmentSegment Segment { get; set; }

    /// <summary>Phase D3B3A: povijest potrošnje paketa ovog sudjelovanja (ledger, najviše jedan aktivan zapis) —
    /// učitava se uvijek sa sudjelovanjem (AutoInclude); čitaj kroz Utils.PackageConsumptions.</summary>
    public List<PackageConsumption> PackageConsumptions { get; set; } = new();

    /// <summary>Phase D3B3B: CheckoutItem stavke usluge koje namiruju OVO sudjelovanje (kroz vrijeme, svih checkouta).
    /// Izvor istine za namirenje je Utils.ParticipationSettlement (aktivne alokacije preko ovih stavki).</summary>
    public List<CheckoutItem> CheckoutItems { get; set; } = new();

    /// <summary>P1 (D5) — ledger posljedica politike ovog sudjelovanja (najviše jedna Active) — učitava se uvijek sa
    /// sudjelovanjem (AutoInclude) jer o njemu ovisi dug (ParticipationSettlement); čitaj kroz Utils.PolicyConsequences.</summary>
    public List<ParticipationPolicyConsequence> PolicyConsequences { get; set; } = new();

    /// <summary>P2 (2D) — projekcija pokrića članarinom (null = klijent nema članarinu relevantnu za termin). Učitava se uvijek
    /// sa sudjelovanjem (AutoInclude) jer o njoj ovisi dug (ParticipationSettlement); piše je samo IMembershipCoverageService.</summary>
    public ParticipationMembershipCoverage MembershipCoverage { get; set; }
}
