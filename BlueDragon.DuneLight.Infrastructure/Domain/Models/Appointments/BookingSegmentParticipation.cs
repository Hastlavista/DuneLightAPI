using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Klijent Bookinga sudjeluje u JEDNOM segmentu istog termina (ciljna izvršna i cjenovna jedinica). Od Phase D3B1 je
/// AUTORITATIVAN izvor izvršnog životnog ciklusa (Status, StatusVersion, dolazak, razlog/klasifikacija otkazivanja) —
/// svaki produkcijski Booking ima točno jedno sudjelovanje (BookingFactory), a čita/piše se kroz
/// Utils.BookingParticipations/BookingLifecycle. Od Phase D3B2 je AUTORITATIVAN i za cijenu (Amount, SuggestedAmount,
/// IsAmountManuallyOverridden — NOT NULL; Booking više nema cijenu), čita se kroz BookingParticipations.AmountOf/...,
/// mijenja kroz BookingFactory (nastanak) i Utils.BookingPrice (re-cijenjenje). Paket i naplata ostaju na Bookingu.
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
/// Otkazivanje: CancellationReason i IsLateCancellation isti su koncepti kao na Bookingu — kasno otkazivanje je
/// klasifikacija uz Status=Cancelled, ne status. Dolazak (ArrivedAt/ArrivedBy) je metapodatak, ne status.
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

    [Column("cancellation_reason")]
    public string CancellationReason { get; set; }

    [Column("is_late_cancellation")]
    public bool? IsLateCancellation { get; set; }

    [Column("base_amount")]
    public decimal? BaseAmount { get; set; }

    [Column("base_amount_source")]
    public PriceSource? BaseAmountSource { get; set; }

    [Column("adjustment_amount")]
    public decimal? AdjustmentAmount { get; set; }

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
}
