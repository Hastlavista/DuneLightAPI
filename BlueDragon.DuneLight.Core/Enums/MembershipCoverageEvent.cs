namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2D) — događaj koji je zadnji promijenio odluku o pokriću sudjelovanja (objašnjivost: "zašto se promijenilo").</summary>
public enum MembershipCoverageEvent
{
    /// <summary>Rezervacija ili ponovna aktivacija sudjelovanja.</summary>
    Booking,
    /// <summary>Prodaja članarine — postojeće buduće rezervacije (2D).</summary>
    MembershipSale,
    /// <summary>Oslobođeno mjesto u limitu istog članstva (2D).</summary>
    SlotFreed,
    /// <summary>Obnova / pomak horizonta (Q27).</summary>
    Renewal,
    /// <summary>Promjena stanja duga (istek grace perioda ili plaćanje/otpis, Q15).</summary>
    DebtChanged,
    /// <summary>Storno novčane uplate sudjelovanja (2D: ponovna evaluacija).</summary>
    PaymentChanged,
    /// <summary>Pauza, otkaz, raniji izlazak, promjena plana ili poništavanje članstva.</summary>
    MembershipChanged,
    /// <summary>Promjena vremena termina (Q6.3).</summary>
    Rescheduled,
    /// <summary>Otkazivanje sudjelovanja.</summary>
    ParticipationCancelled,
    /// <summary>Događaj P1 politike (kasni otkaz, izostanak, otpis, korekcija).</summary>
    PolicyEvent,
    /// <summary>2E (pregled #4) — usklađivanje zastarjele cijene (PriceStale) prije naplate ili u pozadinskom prolazu.</summary>
    PriceRefresh
}
