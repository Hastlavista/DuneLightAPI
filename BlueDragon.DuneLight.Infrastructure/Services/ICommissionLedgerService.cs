using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Infrastructure-only strana provizije — metode s <see cref="IUnitOfWork"/> parametrom pozivaju se IZ TUĐE
/// transakcije (AppointmentService/CheckoutService), kao dio ISTE atomične cjeline kao prijelaz koji zarađuje
/// proviziju (vidi spec section 27/28) — isti obrazac kao IPaymentLedgerService. Odvojeno od Core
/// ICommissionService iz istog razloga kao ondje (Core ne smije referencirati Infrastructure.UnitOfWork).
/// Jedan CommissionService implementira ICommissionRuleService, ICommissionService i ovo sučelje.
///
/// Svaka metoda je no-op (bez iznimke) ako ne postoji primjenjiva aktivna CommissionRule ili se ne može pouzdano
/// odrediti Employee — nedostatak provizije NIKAD ne smije blokirati poslovni prijelaz koji je pokreće (vidi
/// spec section 29). Idempotencija je DB-garantirana (unique indeksi na CommissionEntry, vidi migraciju), NE
/// aplikacijskim "hvati pa preskoči" — pozivatelji već zaključavaju/provjeravaju status izvornog retka PRIJE
/// poziva ovim metodama, pa je dupli upis u praksi nedostižan; ako se svejedno dogodi, cijela pozivateljeva
/// transakcija (uklj. izvorni completion) se vraća natrag umjesto tihog djelomičnog uspjeha (vidi
/// CommissionService.TryAdd, spec section 28/57).
/// </summary>
public interface ICommissionLedgerService
{
    /// <summary>Individualna usluga — jedan odrađen Booking (Status upravo postavljen na Completed od
    /// pozivatelja, PRIJE poziva ovoj metodi jer FK commission_entries.booking_id zahtijeva već persistiran
    /// redak) = jedan izvor. Employee = appointment.EmployeeId, osnovica = booking.Amount (retail vrijednost
    /// izvedenog rada, neovisno o paket-pokriću/nenaplaćenosti — vidi spec section 15/51).</summary>
    Task GenerateForIndividualServiceCompletion(IUnitOfWork uow, Guid organizationId, Appointment appointment, Booking booking);

    /// <summary>Grupna usluga — jedan odrađen grupni termin (Appointment.Status upravo postavljen na Completed)
    /// = jedan izvor, PO TERMINU ne po sudioniku (vidi CommissionSourceType.GroupService domensku napomenu za
    /// obrazloženje). No-op ako termin nema dodijeljenog trenera (appointment.EmployeeId je null za grupne
    /// termine bez zadanog trenera) ili ne postoji primjenjivo Fixed pravilo (Percentage je odbijen već kod
    /// kreiranja pravila za Group-mode usluge, vidi CommissionRuleService).</summary>
    Task GenerateForGroupServiceCompletion(IUnitOfWork uow, Guid organizationId, Appointment appointment);

    /// <summary>Prodaja Producta/Packagea — jedna CheckoutItem stavka (Type=Product ili Package) na upravo
    /// Completed Checkoutu = jedan izvor. Prodavatelj se razrješava iz completedByUserId preko postojeće
    /// User→Employee veze (Employee.UserId, jedinstveno) — no-op za CIJELI checkout ako se ne razriješi na
    /// aktivnog Employeea (vidi spec section 19/55, ne nagađa se preko imena/emaila).</summary>
    Task GenerateForCheckoutCompletion(IUnitOfWork uow, Guid organizationId, Guid completedByUserId, Checkout checkout);
}
