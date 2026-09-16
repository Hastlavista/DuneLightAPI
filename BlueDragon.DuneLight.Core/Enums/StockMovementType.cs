namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Vrsta StockMovement retka — vidi StockMovement.cs. StockMovement je jedini izvor istine za promjenu
/// zaliha (ProductStock.Quantity je materijalizirano trenutno stanje izvedeno iz zbroja ovih redaka, ne
/// mijenja se nikad bez pripadajućeg movementa, vidi ProductStockHandler).
///
/// Initial: prvo postavljanje zalihe za par Product+Company (ProductStock redak tek stvoren).
/// Adjustment: ručna korekcija postojeće zalihe (npr. fizički popis) — staff unosi novo apsolutno stanje,
///     servis izračunava deltu (vidi StockService.Adjust).
/// Sale: automatska prodaja pri Checkout.Complete za Product stavku — QuantityDelta uvijek negativan,
///     CheckoutItemId popunjen (djelomični unique indeks sprječava dvostruki decrement na retry, vidi
///     migraciju).
/// SaleReversal: REZERVIRANO za buduć povrat/refund tok (nije implementirano ovim zahvatom — vidi spec
///     section 31). Ne generira se nigdje u kodu, samo deklarirano da buduća migracija ne mora dirati postojeći
///     string stupac.
/// TransferOut / TransferIn: par redaka koji dijele TransferCorrelationId, opisuju premještaj iste količine
///     između dvije Company (vidi StockService.Transfer).
/// </summary>
public enum StockMovementType
{
    Initial,
    Adjustment,
    Sale,
    SaleReversal,
    TransferOut,
    TransferIn
}
