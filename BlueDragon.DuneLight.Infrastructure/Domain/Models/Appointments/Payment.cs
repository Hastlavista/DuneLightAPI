using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Jedan MONETARNI namirni događaj nad jednim Checkoutom (vidi Checkout.cs) — Checkout nosi komercijalnu
/// obvezu preko svojih CheckoutItem stavki, Payment je stvaran novac primljen za NJIH. Jedan Payment (npr.
/// jedna kartična transakcija) može odjednom namiriti VIŠE stavki (split po stavkama preko PaymentAllocation,
/// vidi PaymentAllocation.cs) — to je razlog zašto Payment ne referencira Booking izravno (staro
/// Payment.BookingId je uklonjeno, vidi Migration_2026_09_17_CheckoutFoundation, "jedan izvor istine" umjesto
/// dva paralelna financijska modela). Više Paymenta po Checkoutu je normalno (partial/split — npr. 40 €
/// gotovina + 60 € kartica), zbroj alokacija aktivnih (Status=Completed) Paymenta nikad ne smije premašiti
/// Checkout MonetaryDue (vidi PaymentAllocation/CheckoutFinancialsCalculator).
///
/// Paket-pokriće (CheckoutItem.Type=Booking uz paket-pokriven Booking) NIKAD ne stvara Payment redak — ulazak
/// iz paketa nije novac. Isto tako stavka s MonetaryDue=0 ne treba Payment.
///
/// Payment je NAKON kreiranja financijska povijest — Amount/Method/CheckoutId se NE mijenjaju naknadno.
/// Ispravka pogreške ide kroz Void (Status -> Voided, isključuje se iz PaidAmount preko svojih alokacija) +
/// nov ispravan Payment, nikad prepisivanje postojećeg retka. Void != Refund: Void znači "ovaj zapis je bio
/// pogrešan/ne broji se", Refund (odgođeno, nije implementirano ovim zahvatom) bi značio "novac je stvarno
/// vraćen nakon što je stvarno primljen". Void kroz redovan POS endpoint dopušten je SAMO dok je roditeljski
/// Checkout Open (vidi spec section 48/CheckoutService.VoidPayment) — Completed Checkout je financijski
/// nepromjenjiv. Iznimka je IPaymentLedgerService.VoidCheckInGeneratedPayments (interno poništenje pogrešnog
/// check-ina preko AppointmentService/BookingService), koje smije voidati i Payment čiji je auto-generirani
/// jednostavačni Checkout već Completed — taj Checkout je implementacijski detalj check-in toka, ne stvaran
/// POS košarica koju osoblje uređuje (vidi IPaymentLedgerService.RecordPayment).
/// </summary>
[Table("payments")]
public class Payment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("checkout_id")]
    public Guid CheckoutId { get; set; }

    /// <summary>Uvijek &gt; 0 — Payment za nulti iznos se ne stvara (vidi klasnu napomenu).</summary>
    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("method")]
    public PaymentMethod Method { get; set; }

    [Column("status")]
    public PaymentStatus Status { get; set; }

    [Column("note")]
    public string Note { get; set; }

    /// <summary>True kad je Payment (i njegov jednostavačni Checkout) automatski stvoren tijekom check-ina/
    /// completiona (BookingService.ResolveCoverage, AppointmentService.CompleteNew/CompleteExisting preko
    /// PaymentMethod na Settlement/BookingSetStatusRequest), false kad je stvoren eksplicitnim pozivom na
    /// Checkout Payment API (ICheckoutService.RecordPayment). Jedina svrha: kad se poništava POGREŠAN check-in
    /// (Completed -&gt; natrag), voidati SAMO Payment koji je TAJ check-in stvorio, nikad ručno dodane Paymente
    /// (vidi IPaymentLedgerService.VoidCheckInGeneratedPayments) — stanje-mašina jamči najviše jedan aktivan
    /// check-in-generated Payment odjednom po Bookingu.</summary>
    [Column("is_checkin_generated")]
    public bool IsCheckInGenerated { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("voided_at")]
    public DateTimeOffset? VoidedAt { get; set; }

    [Column("voided_by")]
    public Guid? VoidedBy { get; set; }

    [Column("void_reason")]
    public string VoidReason { get; set; }

    public Checkout Checkout { get; set; }

    public List<PaymentAllocation> Allocations { get; set; } = new();
}
