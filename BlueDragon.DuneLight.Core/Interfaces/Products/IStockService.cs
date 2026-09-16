using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Products;

namespace BlueDragon.DuneLight.Core.Interfaces.Products;

/// <summary>
/// Kontroler-facing strana zalihe (čitanje + ručna korekcija/transfer) — vidi ProductStock.cs/StockMovement.cs
/// za punu domensku napomenu. Automatska prodajna konzumacija zalihe (Checkout.Complete) ide preko
/// Infrastructure-only IStockLedgerService (isti obrazac kao IPaymentService/IPaymentLedgerService), ne ovdje.
/// </summary>
public interface IStockService
{
    /// <summary>Sva stanja zalihe jedne Company (svi Producti koji imaju redak, vidi spec section 37).</summary>
    Task<List<ProductStockDto>> GetByCompany(Guid organizationId, Guid companyId);

    /// <summary>Sva stanja zalihe jednog Producta po Company (npr. "Zagreb: 12, Split: 4", vidi spec section 9/37).</summary>
    Task<List<ProductStockDto>> GetByProduct(Guid organizationId, Guid productId);

    /// <summary>Puna povijest kretanja zalihe jednog Producta, najnoviji prvi (vidi spec section 36).</summary>
    Task<List<StockMovementDto>> GetMovements(Guid organizationId, Guid productId);

    /// <summary>Postavlja zalihu na apsolutnu vrijednost — server izračunava deltu i sprema Adjustment (ili
    /// Initial za prvi redak) StockMovement, concurrency-safe (zaključava redak, vidi spec section 16/59/67).
    /// Zahtijeva aktivnu Company; Product smije biti neaktivan (ručna korekcija radi usklađenja i dalje dopuštena,
    /// vidi spec section 47).</summary>
    Task<ProductStockDto> Adjust(Guid organizationId, Guid userId, Guid productId, Guid companyId, StockAdjustRequest request);

    /// <summary>Atomski premješta količinu istog Producta između dvije Company (TransferOut/TransferIn par,
    /// zajednički TransferCorrelationId) — zaključava oba retka u determinističkom redoslijedu (vidi spec
    /// section 32-35).</summary>
    Task<StockTransferResultDto> Transfer(Guid organizationId, Guid userId, StockTransferRequest request);
}
