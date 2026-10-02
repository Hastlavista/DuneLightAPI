using System;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Cjenovno stanje jednog (jedinog) sudjelovanja Bookinga — vidi <see cref="Utils.BookingFactory"/> (nastanak) i
/// <see cref="Utils.ParticipationPrice"/> (re-cijenjenje). Vrijednosti razrješava pozivatelj (IPricingService + eventualni
/// ručni iznos); ovaj tip ih samo prenosi, ne računa.
///
/// Phase D3B2: BaseAmount/BaseAmountSource su ISTINIT snapshot razrješavanja cjenika (ResolvePriceResponse.Price/Source)
/// i popunjeni su SAMO kad cijena dolazi iz razrješavanja; inače (npr. <see cref="Zero"/>) ostaju NULL. Trenutni model
/// nema eksplicitnu prilagodbu: SuggestedAmount = razriješena cijena, pa se AdjustmentAmount ne piše (ostaje NULL).
/// Phase M1G: uz snapshot razrješavanja ide i IZVOR cijene (PricingMode/PricingEmployeeId) koji je razrješavanje koristilo.
/// </summary>
public readonly record struct BookingPricing(
    decimal Amount, decimal SuggestedAmount, bool IsAmountManuallyOverridden, decimal? BaseAmount = null, PriceSource? BaseAmountSource = null,
    SegmentPricingMode? PricingMode = null, Guid? PricingEmployeeId = null)
{
    /// <summary>Po razriješenoj cijeni uz opcionalni ručni iznos — isto pravilo kao prije na Bookingu: Amount = ručni ??
    /// predloženi, ručni iznos jednak predloženom NIJE ručna promjena.</summary>
    public static BookingPricing FromResolution(ResolvePriceResponse resolved, decimal? manualAmount) => new(
        manualAmount ?? resolved.Price,
        resolved.Price,
        manualAmount.HasValue && manualAmount.Value != resolved.Price,
        resolved.Price,
        resolved.Source,
        resolved.EmployeeId.HasValue ? SegmentPricingMode.Employee : SegmentPricingMode.Standard,
        resolved.EmployeeId);

    /// <summary>Booking po predloženoj cijeni, bez ručnog iznosa (grupni termin, gost, lista čekanja, /recurring, AddBooking).</summary>
    public static BookingPricing AtSuggested(ResolvePriceResponse resolved) => FromResolution(resolved, null);

    /// <summary>Nulta cijena koja NE dolazi iz razrješavanja cjenika (poništen grupni check-in; privremeno stanje novog
    /// retka u CompleteExisting prije stvarnog cijenjenja) — bez snapshota razrješavanja.</summary>
    public static BookingPricing Zero => new(0m, 0m, false);
}
