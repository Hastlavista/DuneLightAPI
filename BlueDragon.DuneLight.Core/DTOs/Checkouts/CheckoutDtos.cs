using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.DTOs.Checkouts;

/// <summary>Otvara novi Checkout za jednog Klijenta u jednoj Company — bez totala (server sve izvodi, vidi
/// spec section 71). Praznu Checkout je dopušteno otvoriti (stavke se dodaju naknadno).</summary>
public class CheckoutCreateRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    public Guid CompanyId { get; set; }
}

/// <summary>Dodaje postojeći Booking kao stavku — server snapshotta cijenu/opis, klijent ne šalje iznos
/// (vidi spec section 72).</summary>
public class CheckoutAddBookingItemRequest
{
    [Required]
    public Guid BookingId { get; set; }
}

/// <summary>Dodaje kupnju Paketa kao stavku — cijena se razrješava preko IPricingService u trenutku dodavanja
/// (Company iz Checkouta), klijent ne šalje iznos (vidi spec section 73).</summary>
public class CheckoutAddPackageItemRequest
{
    [Required]
    public Guid PackageId { get; set; }
}

/// <summary>Dodaje Product stavku (ili povećava Quantity postojeće aktivne Product stavke za isti proizvod,
/// vidi spec section 27) — server razrješava UnitPrice iz Product.DefaultPrice i snapshotta, klijent ne šalje
/// iznos (vidi spec section 66). Jedini CheckoutItemType kod kojeg Quantity smije biti &gt; 1.</summary>
public class CheckoutAddProductItemRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Količina mora biti veća od nule.")]
    public int Quantity { get; set; } = 1;
}

/// <summary>Eksplicitna ručna alokacija jednog dijela Paymenta na jednu stavku — opcionalno, vidi
/// CheckoutPaymentCreateRequest.Allocations (spec section 36).</summary>
public class CheckoutPaymentAllocationRequest
{
    [Required]
    public Guid CheckoutItemId { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Iznos alokacije mora biti veći od 0.")]
    public decimal Amount { get; set; }
}

/// <summary>Bilježi novčanu naplatu na Checkoutu — ako Allocations izostavljen, server automatski raspoređuje
/// FIFO po redoslijedu dodavanja stavki (vidi spec section 34/35). Ako je zadan, mora točno pokriti Amount i
/// ne smije premašiti preostali dug pojedine stavke (vidi spec section 36).</summary>
public class CheckoutPaymentCreateRequest
{
    [Range(0.01, double.MaxValue, ErrorMessage = "Iznos mora biti veći od 0.")]
    public decimal Amount { get; set; }

    [Required]
    public PaymentMethod Method { get; set; }

    [MaxLength(500)]
    public string Note { get; set; }

    /// <summary>Opcionalno — izostavi za automatsku FIFO raspodjelu (preporučeno za normalan POS unos).</summary>
    public List<CheckoutPaymentAllocationRequest> Allocations { get; set; }
}

/// <summary>Poništenje pogrešno unesenog Paymenta — dopušteno samo dok je Checkout Open (vidi spec section 48).</summary>
public class CheckoutPaymentVoidRequest
{
    [MaxLength(500)]
    public string Reason { get; set; }
}

/// <summary>Otkazivanje Bookinga/Cancel-razlog nije ovdje — vidi BookingCancelRequest. Ovaj DTO je rezerviran
/// za buduće Cancel-razlog polje na Checkoutu ako zatreba; trenutno bez tijela (POST bez requesta).</summary>
public class CheckoutCancelRequest
{
}

/// <summary>Izvedeni financijski sažetak jedne CheckoutItem stavke — vidi CheckoutFinancialsCalculator.</summary>
public class CheckoutItemDto
{
    public Guid Id { get; set; }
    public CheckoutItemType Type { get; set; }
    public string Description { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    /// <summary>Puna maloprodajna vrijednost stavke, bez obzira na način podmirenja.</summary>
    public decimal RetailAmount { get; set; }

    /// <summary>Koliko se stavke stvarno duguje u novcu — 0 za paket-pokriven Booking (vidi spec section 22/23).</summary>
    public decimal MonetaryDue { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }

    public Guid? BookingId { get; set; }
    public Guid? PackageId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ClientPackageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

/// <summary>Totali cijelog Checkouta — vidi CheckoutFinancialsCalculator.</summary>
public class CheckoutTotalsDto
{
    public decimal RetailTotal { get; set; }
    public decimal MonetaryDue { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public bool IsFullyPaid { get; set; }
}

public class CheckoutDto
{
    public Guid Id { get; set; }
    public CheckoutStatus Status { get; set; }

    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; }

    public Guid ClientId { get; set; }
    public string ClientName { get; set; }

    public List<CheckoutItemDto> Items { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
    public CheckoutTotalsDto Totals { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
}
