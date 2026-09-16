using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;

/// <summary>
/// Jedan komercijalni checkout/cart događaj — korijenski agregat POS temelja (vidi spec). Zamjenjuje
/// direktnu Payment.BookingId vlasništvo: monetarni Payment sada pripada Checkoutu (Payment.CheckoutId), ne
/// izravno Bookingu — jedan Payment (npr. jedna kartična transakcija) može namiriti VIŠE komercijalnih
/// stavki (CheckoutItem) odjednom preko PaymentAllocation (vidi PaymentAllocation.cs).
///
/// Jedan Checkout = jedan Client (MVP, vidi spec section 6) i jedna Company (mjesto transakcije, vidi spec
/// section 7/43) — sve CheckoutItem stavke (Booking/Package) moraju pripadati OVOM Clientu i (za Booking)
/// terminu čija Appointment.CompanyId == ovo CompanyId.
///
/// Lifecycle: Open -&gt; Completed (kad je MonetaryDue u potpunosti namiren preko Payment/PaymentAllocation
/// ili paket-pokrića) ili Open -&gt; Cancelled (prazan/nenaplaćen, ili nakon što su svi Paymenti voidani).
/// Nakon Completed/Cancelled je financijski nepromjenjiv — vidi CheckoutService.
///
/// Completed -&gt; Voided je USKA, isključivo interna tranzicija (vidi CheckoutStatus.Voided domensku napomenu)
/// — nikad kroz redovan POS API, samo kroz IPaymentLedgerService.VoidCheckInGeneratedPayments kad se poništava
/// pogrešan check-in čiji je auto-generirani jednostavačni Checkout (točno jedna Booking stavka, točno jedan
/// check-in-generated Payment) već Completed. Bez ove tranzicije bi takav Checkout ostao "Completed" uz
/// OutstandingAmount &gt; 0 (jer je njegov jedini Payment voidan), krseći temeljni invarijant "Completed =
/// financijski namiren" — Voided je izlaz iz tog invarijanta za povijesno-poništen slučaj, bez pretvaranja
/// natrag u Open (nema ponovnog uređivanja stavki/plaćanja).
/// </summary>
[Table("checkouts")]
public class Checkout
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public Guid? Id { get; set; }

    [Column("organization_id")]
    public Guid OrganizationId { get; set; }

    [Column("company_id")]
    public Guid CompanyId { get; set; }

    [Column("client_id")]
    public Guid ClientId { get; set; }

    [Column("status")]
    public CheckoutStatus Status { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [Column("completed_by")]
    public Guid? CompletedBy { get; set; }

    [Column("cancelled_at")]
    public DateTimeOffset? CancelledAt { get; set; }

    [Column("cancelled_by")]
    public Guid? CancelledBy { get; set; }

    /// <summary>Popunjeno SAMO za Completed -&gt; Voided internu korekcijsku tranziciju (vidi CheckoutStatus.Voided) —
    /// nikad za normalan POS Cancel (koristi CancelledAt/By).</summary>
    [Column("voided_at")]
    public DateTimeOffset? VoidedAt { get; set; }

    [Column("voided_by")]
    public Guid? VoidedBy { get; set; }

    [Column("void_reason")]
    public string VoidReason { get; set; }

    public Company Company { get; set; }
    public Client Client { get; set; }

    public List<CheckoutItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}
