using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Checkouts;

namespace BlueDragon.DuneLight.Core.Interfaces.Checkouts;

/// <summary>
/// POS/Checkout temelji — vidi Checkout.cs (Infrastructure) za punu domensku napomenu. Jedan Checkout = jedan
/// Client + jedna Company; sadrži CheckoutItem stavke (Booking/Package) i Payment(e) koji ih namiruju preko
/// PaymentAllocation. Sve mutacije su dopuštene samo dok je Checkout Open — Completed/Cancelled su terminalni
/// (vidi spec section 50).
/// </summary>
public interface ICheckoutService
{
    Task<CheckoutDto> Create(Guid organizationId, Guid userId, CheckoutCreateRequest request);

    Task<CheckoutDto> GetById(Guid organizationId, Guid id);

    /// <summary>Puna povijest Checkouta klijenta, najnoviji prvi — za Povijest klijenta (spec section 77).</summary>
    Task<List<CheckoutDto>> GetByClient(Guid organizationId, Guid clientId);

    /// <summary>Dodaje postojeći Booking kao stavku — snapshotta Description/Amount iz trenutnog stanja Bookinga.
    /// Odbija ako Booking pripada drugom Klijentu/Company, je Cancelled, ili je već aktivna stavka u drugom Open
    /// checkoutu (BOOKING_ALREADY_IN_OPEN_CHECKOUT, vidi spec section 29).</summary>
    Task<CheckoutDto> AddBookingItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddBookingItemRequest request);

    /// <summary>Dodaje kupnju Paketa kao stavku — cijena se razrješava preko IPricingService (Company iz
    /// Checkouta) i snapshotta odmah; NE izdaje ClientPackage (to se događa tek na Complete, vidi spec section 24).</summary>
    Task<CheckoutDto> AddPackageItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddPackageItemRequest request);

    /// <summary>Dodaje Product stavku — cijena se razrješava iz Product.DefaultPrice i snapshotta odmah. Ako
    /// Checkout već ima aktivnu Product stavku za isti Product, umjesto nove stavke povećava Quantity/Amount
    /// postojeće (jedna stavka po Productu po Checkoutu, vidi Products &amp; Stock spec section 27). NE dira
    /// zalihu (konzumacija se događa tek na Complete, vidi spec section 23/25).</summary>
    Task<CheckoutDto> AddProductItem(Guid organizationId, Guid userId, Guid checkoutId, CheckoutAddProductItemRequest request);

    /// <summary>Uklanja stavku iz Open checkouta — blokirano ako stavka ima aktivnu (Completed Payment) alokaciju
    /// (CHECKOUT_ITEM_HAS_ALLOCATIONS, vidi spec section 33).</summary>
    Task<CheckoutDto> RemoveItem(Guid organizationId, Guid userId, Guid checkoutId, Guid itemId);

    /// <summary>Bilježi novčanu naplatu — automatska FIFO raspodjela po redoslijedu dodavanja stavki ako
    /// Allocations izostavljen, inače eksplicitna raspodjela (vidi CheckoutPaymentCreateRequest). Concurrency-safe
    /// (zaključava Checkout redak prije ponovnog čitanja/validacije, vidi spec section 34/59).</summary>
    Task<CheckoutDto> RecordPayment(Guid organizationId, Guid userId, Guid checkoutId, CheckoutPaymentCreateRequest request);

    /// <summary>Poništava Payment — dopušteno SAMO dok je Checkout Open (vidi spec section 48).</summary>
    Task<CheckoutDto> VoidPayment(Guid organizationId, Guid userId, Guid checkoutId, Guid paymentId, CheckoutPaymentVoidRequest request);

    /// <summary>Zatvara Checkout — zahtijeva MonetaryDue u potpunosti namiren (paket-pokriće ili Payment
    /// alokacije). Atomarno izdaje ClientPackage za svaku Package stavku (idempotentno — CheckoutItem.ClientPackageId
    /// sprječava dvostruko izdavanje, vidi spec section 26/61/62). Nakon Completed, Checkout je nepromjenjiv.</summary>
    Task<CheckoutDto> Complete(Guid organizationId, Guid userId, Guid checkoutId);

    /// <summary>Otkazuje Open checkout — dopušteno samo ako nema aktivnih (Completed) Paymenta (vidi spec section 49).</summary>
    Task<CheckoutDto> Cancel(Guid organizationId, Guid userId, Guid checkoutId);
}
