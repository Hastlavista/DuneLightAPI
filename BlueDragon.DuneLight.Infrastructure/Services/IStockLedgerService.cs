using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Infrastructure-only strana zalihe — poziva se IZ TUĐE transakcije (CheckoutService.Complete), kao dio iste
/// atomične cjeline kao Checkout completion, isti obrazac kao IPaymentLedgerService/IWaitlistPromotionService.
/// Odvojeno od Core IStockService iz istog razloga kao ondje (Core ne smije referencirati Infrastructure.
/// UnitOfWork/Domain modele). Jedan StockService implementira oba sučelja.
/// </summary>
public interface IStockLedgerService
{
    /// <summary>Validira i konzumira zalihu za SVE Product CheckoutItem stavke danog Checkouta unutar
    /// pozivateljeve transakcije — zaključava svaki (ProductId, CompanyId) ProductStock redak u determinističkom
    /// redoslijedu (sortirano po ProductId, vidi spec section 26), provjerava dostatnost SVIH prije bilo kakvog
    /// decrementa, tek onda dekrementira i stvara po jedan Sale StockMovement po stavci. Baca
    /// BusinessRuleException(INSUFFICIENT_STOCK) ako bilo koji Product nema dovoljno zalihe — pozivateljeva
    /// transakcija se ne commita (vidi CheckoutService.Complete), pa nema djelomičnog decrementa (spec section 25).
    /// Ne radi ništa (no-op) ako productItems prazno.</summary>
    Task ConsumeForSale(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid companyId, List<CheckoutItem> productItems, DateTimeOffset now);
}
