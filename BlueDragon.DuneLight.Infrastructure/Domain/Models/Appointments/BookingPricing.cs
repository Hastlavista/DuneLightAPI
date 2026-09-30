namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Početni komercijalni snapshot novog Bookinga (Amount/SuggestedAmount/IsAmountManuallyOverridden) — vidi
/// <see cref="Utils.BookingFactory"/>. Vrijednosti razrješava pozivatelj (IPricingService + eventualni ručni iznos);
/// ovaj tip ih samo prenosi, ne računa.
/// </summary>
public readonly record struct BookingPricing(decimal Amount, decimal SuggestedAmount, bool IsAmountManuallyOverridden)
{
    /// <summary>Booking po predloženoj cijeni, bez ručnog iznosa (grupni termin, gost, lista čekanja, /recurring, AddBooking).</summary>
    public static BookingPricing AtSuggested(decimal suggestedAmount) => new(suggestedAmount, suggestedAmount, false);
}
