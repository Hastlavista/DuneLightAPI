using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICheckoutHandler
{
    /// <summary>checkout.Items/Payments (i item.Allocations za check-in-generated jednostavačni checkout, vidi
    /// IPaymentLedgerService.RecordPayment) moraju biti popunjeni prije poziva ako se cijeli graf stvara odjednom
    /// — cascade insert, isto ponašanje kao IAppointmentHandler.Add(appointment).</summary>
    Task Add(IUnitOfWork uow, Checkout checkout);

    /// <summary>Zaključava Checkout redak (SELECT ... FOR UPDATE), bare redak bez includa — koristi CheckoutService
    /// prije svake mutacije (item/payment/complete/cancel) da konkurentni zahtjevi nad ISTIM checkoutom čekaju na
    /// lock pa svježe čitaju nakon commita prvog (isti obrazac kao IPaymentHandler.GetBookingForUpdate/
    /// IAppointmentHandler.GetForUpdateWithGroup). Null ako ne postoji.</summary>
    Task<Checkout> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Puni graf (Company, Client, Items.Booking, Items.Allocations.Payment, Payments) unutar zajedničke
    /// transakcije — poziva se NAKON GetForUpdate da izračun totala/validacija vidi svježe stanje.</summary>
    Task<Checkout> GetGraph(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Kao <see cref="GetGraph(IUnitOfWork, Guid, Guid)"/>, ali u vlastitom kontekstu — za read-only GET.</summary>
    Task<Checkout> GetGraph(Guid organizationId, Guid id);

    /// <summary>Puna povijest Checkouta klijenta (puni graf), najnoviji prvi — za Povijest klijenta (spec section 77).</summary>
    Task<List<Checkout>> GetByClient(Guid organizationId, Guid clientId);

    /// <summary>Sprema skalarne promjene na Checkoutu (Status/CompletedAt/CancelledAt i sl.) — bez diranja Items/Payments.</summary>
    Task Update(IUnitOfWork uow, Checkout checkout);

    /// <summary>Umeće novu stavku — može baciti DbUpdateException na povredu ux_checkout_items_locks_booking
    /// (isti Booking već aktivan u drugom Open checkoutu, vidi CheckoutItem.LocksBooking), koju CheckoutService
    /// hvata i pretvara u BOOKING_ALREADY_IN_OPEN_CHECKOUT.</summary>
    Task AddItem(IUnitOfWork uow, CheckoutItem item);

    /// <summary>Trajno briše stavku (dopušteno samo dok nema aktivnih alokacija, vidi spec section 33) — poziva
    /// CheckoutService nakon provjere.</summary>
    Task RemoveItem(IUnitOfWork uow, CheckoutItem item);

    /// <summary>Jedna stavka s uključenim Allocations.Payment (za provjeru postojećih alokacija prije uklanjanja
    /// i za financijski izračun) i Booking — null ako ne postoji ili ne pripada ovom Checkoutu/organizaciji.</summary>
    Task<CheckoutItem> GetItem(IUnitOfWork uow, Guid organizationId, Guid checkoutId, Guid itemId);

    /// <summary>Umeće novi Payment — payment.Allocations mora biti popunjen prije poziva (cascade insert).</summary>
    Task AddPayment(IUnitOfWork uow, Payment payment);

    /// <summary>Sprema promjene na postojećem Paymentu (Void) — ne dira Allocations.</summary>
    Task UpdatePayment(IUnitOfWork uow, Payment payment);

    Task<Payment> GetPayment(IUnitOfWork uow, Guid organizationId, Guid checkoutId, Guid paymentId);

    /// <summary>Sve CheckoutItem stavke (kroz vrijeme, svih Checkouta) koje referenciraju ovaj Booking, s
    /// uključenim Allocations.Payment — izvor istine za BookingFinancialsCalculator kad Booking.CheckoutItems
    /// nije unaprijed učitan preko Include lanca (vidi IPaymentService.GetForBooking).</summary>
    Task<List<CheckoutItem>> GetItemsForBooking(Guid organizationId, Guid bookingId);

    /// <summary>Kao <see cref="GetItemsForBooking(Guid, Guid)"/>, ali unutar zajedničke transakcije — koristi
    /// IPaymentLedgerService.VoidCheckInGeneratedPayments.</summary>
    Task<List<CheckoutItem>> GetItemsForBooking(IUnitOfWork uow, Guid organizationId, Guid bookingId);

    /// <summary>Svi TRENUTNO Open Checkouti ove Company, puni graf (za CheckoutFinancialsCalculator.Calculate)
    /// — za OperationalDashboardService.Financial (vidi spec section 18: uvijek trenutno stanje, bez obzira na
    /// odabrani dashboard datum, jer stari nenaplaćen Checkout i dalje traži pažnju).</summary>
    Task<List<Checkout>> GetOpenByCompany(Guid organizationId, Guid companyId);

    /// <summary>Zbroj Payment.Amount za Status=Completed čiji roditeljski Checkout pripada ovoj Company, CreatedAt
    /// unutar [dayStart, dayEnd) — jedan skalarni upit (SUM), za OperationalDashboardService.Financial.TodayRevenue
    /// (vidi spec section 15/41). Voided Payment se ne broji (filtrirano Status=Completed).</summary>
    Task<decimal> GetCompletedPaymentAmountForCompanyOnDate(Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd);
}
