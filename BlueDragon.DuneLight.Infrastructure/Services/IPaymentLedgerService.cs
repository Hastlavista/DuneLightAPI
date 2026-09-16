using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Infrastructure-only strana Payment ledgera — metode s <see cref="IUnitOfWork"/> parametrom su namijenjene
/// pozivu IZ TUĐE transakcije (BookingService/AppointmentService check-in/completion tokovi), kao dio iste
/// atomične cjeline kao operacija koja ih pokreće — isti obrazac kao IWaitlistPromotionService. Odvojeno od
/// Core IPaymentService iz istog razloga kao ondje (Core ne smije referencirati Infrastructure.UnitOfWork).
/// Jedan PaymentService implementira oba sučelja.
///
/// Otkad Payment pripada Checkoutu (vidi Payment.cs), RecordPayment interno stvara jednostavačni Checkout
/// (jedan Booking CheckoutItem, odmah Completed jer se uvijek naplaćuje puni iznos u jednom potezu) — vidi
/// implementaciju u PaymentService. Ovo je implementacijski detalj postojećeg check-in toka, NE zamjenjuje
/// stvaran POS Checkout (vidi ICheckoutService) koji staff eksplicitno otvara/uređuje preko API-ja.
/// </summary>
public interface IPaymentLedgerService
{
    /// <summary>Bilježi monetarni Payment za booking unutar POZIVATELJEVE transakcije — booking mora već biti
    /// zaključan (FOR UPDATE) od pozivatelja u ISTOM uow prije poziva (BookingService/AppointmentService već
    /// drže Appointment/Booking retke zaključane preko svojih vlastitih tokova, npr. AppointmentHandler.Add
    /// unutar iste transakcije za novi Appointment — nema konkurentskog rizika za redak koji tek nastaje).
    /// companyId je mjesto transakcije (Appointment.CompanyId u pozivatelja) — postaje Checkout.CompanyId.
    /// isCheckInGenerated=true označava Payment kao automatski stvoren tijekom check-ina/completiona (vidi
    /// Payment.IsCheckInGenerated i VoidCheckInGeneratedPayments) — pozivatelji IZ check-in/completion tokova
    /// (BookingService.ResolveCoverage, AppointmentService.CompleteNew/CompleteExisting) prosljeđuju true.</summary>
    Task<Payment> RecordPayment(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid companyId, Booking booking, PaymentMethod method, decimal amount,
        string note, bool isCheckInGenerated = false);

    /// <summary>Poništava SAMO Payment(e) koje je automatski stvorio check-in (IsCheckInGenerated=true, još
    /// Completed) — koristi se isključivo kad se poništava pogrešan check-in (Completed -&gt; natrag na
    /// Confirmed/drugo, vidi BookingService.ApplyGroupTransition). Ručno dodani Checkout Paymenti
    /// (IsCheckInGenerated=false) OSTAJU netaknuti — undo check-ina reverzira SAMO nuspojave TOG check-ina, ne
    /// cijelu povijest Bookinga (za razliku od cancel/no-show, koji Paymente uopće ne diraju — vidi spec section
    /// 27/28). Namjerno smije voidati Payment i kad je njegov (jednostavačni, auto-generirani) Checkout već
    /// Completed — vidi Payment.cs klasnu napomenu za obrazloženje ove namjerne iznimke od "Void samo dok je
    /// Checkout Open" pravila koje vrijedi za redovan POS endpoint (ICheckoutService.VoidPayment). Stanje-mašina
    /// jamči najviše jedan aktivan check-in-generated Payment u danom trenutku. No-op ako takav Payment ne postoji.</summary>
    Task VoidCheckInGeneratedPayments(IUnitOfWork uow, Guid organizationId, Guid userId, Booking booking, string reason);
}
