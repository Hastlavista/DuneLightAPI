namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Minimalan lifecycle jednog checkout/cart događaja — vidi Checkout.cs. Open je jedino ne-terminalno
/// stanje; Completed/Cancelled/Voided su terminalni (bez povratka). Namjerno bez Pending/Processing (to je
/// briga vanjskog platnog procesora, izvan MVP POS-a — vidi spec).
///
/// Voided je USKA interna korekcijska tranzicija, NE opća "void bilo koji checkout" mogućnost — jedini
/// pozivatelj je IPaymentLedgerService.VoidCheckInGeneratedPayments kad se poništava pogrešan check-in čiji
/// je auto-generirani jednostavačni Checkout već Completed (vidi Checkout.cs domensku napomenu). Completed
/// -> Voided znači "ovaj zatvoren checkout je poništen administrativnom korekcijom povezane operacije, njegov
/// financijski rezultat više ne vrijedi" — razlikuje se od Cancelled (nikad nije bio financijski zatvoren) i
/// NIKAD se ne vraća natrag na Open. Voided checkout ne prolazi normalne mutacije (item/payment/complete/cancel
/// — sve zahtijevaju Status=Open) i ne smije se brojati kao aktivan prihod/namirenje u izvještajima.
/// </summary>
public enum CheckoutStatus
{
    Open,
    Completed,
    Cancelled,
    Voided
}
