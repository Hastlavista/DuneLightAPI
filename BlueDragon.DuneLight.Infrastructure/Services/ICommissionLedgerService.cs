using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
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
    /// <summary>Individualna usluga — jedno odrađeno sudjelovanje (Status upravo postavljen na Completed od pozivatelja,
    /// PRIJE poziva ovoj metodi jer FK zahtijeva već persistiran redak) = jedan izvor. Phase M1G: za SVAKOG zaposlenika
    /// segmenta (execution.EmployeeIds) neovisno — njegovo pravilo, njegov zapis; osnovica postotka = participation.Amount
    /// (konačna cijena, neovisno o paket-pokriću/nenaplaćenosti). Izvor cijene (PricingEmployeeId) NIJE korisnik provizije.
    /// SourceVersion = StatusVersion sudjelovanja; Booking samo kontekst (BookingId stupac).</summary>
    Task GenerateForIndividualServiceCompletion(
        IUnitOfWork uow, Guid organizationId, ParticipationExecutionContext execution, BookingSegmentParticipation participation);

    /// <summary>Grupna sesija — zatvoren (close-out) SEGMENT grupnog occurrencea = izvor, PO SESIJI ne po sudioniku.
    /// Phase M1G: za svakog zaposlenika segmenta jedan Fixed zapis (jedinstven po segmentu i zaposleniku). Segment bez
    /// zaposlenika: ništa. Pravilo koje nije Fixed se NE evaluira — vraća se upozorenje GROUP_COMMISSION_RULE_NOT_SUPPORTED
    /// (nema osnovice za postotak grupne sesije).</summary>
    Task<List<WarningDto>> GenerateForGroupServiceCompletion(IUnitOfWork uow, Guid organizationId, SegmentExecutionContext execution);

    /// <summary>Prodaja Producta/Packagea — jedna CheckoutItem stavka (Type=Product ili Package) na upravo
    /// Completed Checkoutu = jedan izvor. Prodavatelj se razrješava iz completedByUserId preko postojeće
    /// User→Employee veze (Employee.UserId, jedinstveno) — no-op za CIJELI checkout ako se ne razriješi na
    /// aktivnog Employeea (vidi spec section 19/55, ne nagađa se preko imena/emaila).</summary>
    Task GenerateForCheckoutCompletion(IUnitOfWork uow, Guid organizationId, Guid completedByUserId, Checkout checkout);

    /// <summary>Reverzira (Earned -&gt; Reversed) SVE CommissionEntry zapise (Phase M1G: po jedan za svakog zaposlenika segmenta) zarađene TOČNO OVIM completionom individualnog
    /// Bookinga, kao dio BookingService.ApplyIndividualCompletionCorrection (Individual Booking Completed -&gt;
    /// Confirmed administrativna korekcija) — poziva se PRIJE nego sudjelovanje stvarno prijeđe na Confirmed
    /// (pozivatelj još drži Booking pod FOR UPDATE lockom iz iste transakcije). No-op ako aktivan (Earned) zapis
    /// ne postoji (nikad nije bilo primjenjivog CommissionRule kod completiona, ili je već reverziran — idempotentan
    /// retry, vidi spec section 15/41). Identificira izvor isključivo preko sudjelovanja + Status=Earned
    /// (ICommissionEntryHandler.GetActiveForParticipation), nikad po iznosu/datumu/zaposleniku. NE dira BaseAmount/
    /// CalculationType/RuleValue/CommissionAmount (povijesni snapshot ostaje netaknut, vidi spec section 33) — samo
    /// Status/ReversedAt/ReversedBy. Sljedeći completion istog Bookinga (nakon korekcije) zarađuje NOVI Earned
    /// zapis s NOVIM SourceVersion (vidi CommissionEntry.cs), bez sudara sa ovim (sad Reversed) zapisom.</summary>
    Task ReverseForIndividualServiceCorrection(IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation);
}
