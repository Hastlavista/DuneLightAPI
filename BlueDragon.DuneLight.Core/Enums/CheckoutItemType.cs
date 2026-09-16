namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Komercijalni subjekt jedne CheckoutItem stavke — vidi CheckoutItem.cs. Svaka vrijednost ima točno JEDAN
/// odgovarajući tipizirani FK na CheckoutItem (BookingId za Booking, PackageId za Package, ProductId za
/// Product) koji MORA biti popunjen i mora se slagati s Type (vidi CHECK constraint u migraciji). Product je
/// jedini tip kod kojeg Quantity smije biti &gt; 1 (Booking/Package su uvijek Quantity=1, vidi
/// CheckoutService.AddProductItem/spec Products & Stock section 21). Buduće vrijednosti (Membership) dodaju
/// se ovdje + odgovarajući nullable FK, bez potrebe za rewrite Checkout/Payment modela.
/// </summary>
public enum CheckoutItemType
{
    Booking,
    Package,
    Product
}
