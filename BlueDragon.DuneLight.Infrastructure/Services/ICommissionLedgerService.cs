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
/// transakcije (AppointmentService/BookingService/CheckoutService/ClientMembershipService), kao dio ISTE atomične cjeline kao
/// prijelaz koji zarađuje ili stornira proviziju — isti obrazac kao IPaymentLedgerService. Odvojeno od Core ICommissionService
/// iz istog razloga kao ondje (Core ne smije referencirati Infrastructure.UnitOfWork). Jedan CommissionService implementira
/// ICommissionRuleService, ICommissionService i ovo sučelje.
///
/// Svaka metoda je no-op (bez iznimke) ako ne postoji primjenjivo pravilo ili korisnik provizije — nedostatak provizije NIKAD
/// ne blokira poslovni prijelaz koji je pokreće. Idempotencija je DB-garantirana (unique indeksi na CommissionEntry); dupli upis
/// prekida cijelu pozivateljevu transakciju umjesto tihog djelomičnog uspjeha (vidi CommissionService.TryAdd).
///
/// P2 (2F, Vagaro model, ADR-0030): pravilo za uslugu ima prednost pred općim pravilom zaposlenika (sve individualne usluge);
/// "Bez provizije" je izričit izbor; verzija se bira po datumu važenja (deaktivacija bez povratka na stariju); osnovica za
/// odrađeno po postavkama "oduzmi popuste" / "oduzmi popuste članstva"; uz zapis snapshot postavki i objašnjenje izbora pravila.
/// </summary>
public interface ICommissionLedgerService
{
    /// <summary>Individualna usluga — jedno odrađeno sudjelovanje (Status upravo postavljen na Completed, pokriće članarinom i
    /// potrošnja paketa već upisani u istoj transakciji) = jedan izvor. Za SVAKOG zaposlenika segmenta neovisno: njegovo pravilo za
    /// uslugu ili opće pravilo važeće na lokalni datum sesije (kalendar poslovnice termina). Osnovica: cijena sesije (ručni iznos ako
    /// je upisan, inače cjenik); "oduzmi popuste" / "oduzmi popuste članstva" uzimaju cijenu nakon prilagodbe; pokrivena sesija uz
    /// "oduzmi popuste članstva" → 0; paket ne mijenja osnovicu. SourceVersion = StatusVersion sudjelovanja.</summary>
    Task GenerateForIndividualServiceCompletion(
        IUnitOfWork uow, Guid organizationId, ParticipationExecutionContext execution, BookingSegmentParticipation participation);

    /// <summary>Grupna sesija — zatvoren (close-out) SEGMENT grupnog occurrencea = izvor, PO SESIJI ne po sudioniku.
    /// Za svakog zaposlenika segmenta jedan Fixed zapis po pravilu USLUGE važećem na datum sesije (opće pravilo i prekidači osnovice
    /// se ne primjenjuju; "Bez provizije" = ništa). Pravilo koje nije Fixed se NE evaluira — upozorenje GROUP_COMMISSION_RULE_NOT_SUPPORTED.</summary>
    Task<List<WarningDto>> GenerateForGroupServiceCompletion(IUnitOfWork uow, Guid organizationId, SegmentExecutionContext execution);

    /// <summary>Checkout Complete — provizija na prodaju: Product/Package stavke za zaposlenika ODABRANOG na stavci
    /// (CheckoutItem.SaleCommissionEmployeeId, §18.1) po njegovom pravilu za prodaju važećem na datum nastanka; zatim provjera
    /// prve prodaje članarine (Q42) za članstva čija su zaduženja stavke ovog checkouta. Booking stavke i zaduženja obnove: odabir
    /// se samo sprema (Q52).</summary>
    Task GenerateForCheckoutCompletion(IUnitOfWork uow, Guid organizationId, Guid completedByUserId, Checkout checkout);

    /// <summary>P2 (2F, Q42) — provizija na prvu prodaju članarine: kad su zaduženje prvog perioda i početna naknada PRVI PUT
    /// konačni (poziva se na Checkout Complete i otpisu), jednom: osnovica = stvarno plaćeno na njima, korisnik = vrijednost s
    /// članstva, pravilo za prodaju plana važeće na datum nastanka. Ishod se pamti: NoRecipient dopušta naknadnu dodjelu,
    /// NoRule i ZeroBase su konačni. Zaključava članstvo.</summary>
    Task EvaluateMembershipFirstSale(IUnitOfWork uow, Guid organizationId, Guid userId, Guid membershipId);

    /// <summary>P2 (2F, Q38) — usklađuje proviziju za plaćenu P1 naknadu individualnog sudjelovanja s trenutnim stanjem: aktivna
    /// posljedica s naknadom plaćenom u cijelosti → provizija (samo uz postavku WhenFeePaid u trenutku nastanka; pravilo za
    /// odrađeno važeće na datum sesije kao za sesiju; postotak od naknade, Fixed ograničen na naknadu); naknada više nije
    /// plaćena u cijelosti, oproštena ili poništena → storno aktivne provizije. Poziva se nakon promjene uplata, otpisa i
    /// korekcije statusa. No-op za grupne termine i sudjelovanja bez posljedice.</summary>
    Task SyncPolicyFeeCommission(IUnitOfWork uow, Guid organizationId, Guid userId, Guid participationId);

    /// <summary>P2 (2F, §16.3) — promjena korisnika provizije na prvu prodaju članarine PRIJE nastanka provizije (naredba na
    /// članstvu ili kroz stavku zaduženja prve prodaje u otvorenom checkoutu): jedini izvor je članstvo, promjena je događaj u
    /// povijesti članstva (tko, kada, s koga na koga). Nakon nastanka → COMMISSION_SALE_ALREADY_EARNED (korekcija, Q50).
    /// Zaključava članstvo. Vraća true kad se vrijednost promijenila.</summary>
    Task<bool> SetMembershipSaleCommissionEmployee(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid membershipId, Guid? employeeId, string via);

    /// <summary>Reverzira (Earned -&gt; Reversed) SVE IndividualService zapise zarađene TOČNO OVIM completionom sudjelovanja, kao
    /// dio BookingService korekcije Completed -&gt; drugi status. No-op ako aktivan zapis ne postoji. Snapshot se ne dira.</summary>
    Task ReverseForIndividualServiceCorrection(IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation);

    /// <summary>P2 (2F) — korisnik provizije na prodaju mora biti postojeći AKTIVAN zaposlenik u trenutku odabira (INACTIVE_EMPLOYEE).
    /// Kasnija neaktivnost ne poništava odabir.</summary>
    Task EnsureSelectableEmployee(Guid organizationId, Guid employeeId);
}
