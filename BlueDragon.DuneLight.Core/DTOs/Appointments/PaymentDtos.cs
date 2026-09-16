using System;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Appointments;

/// <summary>Jedan monetarni namirni događaj nad Checkoutom — vidi Payment.cs (Infrastructure) za punu domensku
/// napomenu. Zahtjev za kreiranje/void Paymenta (PaymentCreateRequest/PaymentVoidRequest) sada živi u
/// CheckoutDtos jer Payment pripada Checkoutu (ICheckoutService), ne izravno Bookingu.</summary>
public class PaymentDto
{
    public Guid Id { get; set; }
    public Guid CheckoutId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; }
    public string Note { get; set; }

    /// <summary>True = automatski stvoren tijekom check-ina/completiona (vidi Payment.IsCheckInGenerated), false =
    /// ručno dodan preko Checkout POS API-ja. Samo informativno za UI (npr. "auto" oznaka) — klijent ovo ne postavlja.</summary>
    public bool IsCheckInGenerated { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string VoidReason { get; set; }
}
