namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Stvaran način MONETARNOG podmirenja Payment retka — otkad postoji Payment ledger (vidi Payment.cs),
/// Package VIŠE NIJE član ovog enuma: pokriće paketom nije novčana transakcija (vidi Booking.PackageCoverageApplied/
/// ClientPackageId na Booking), pa se ne bilježi kao Payment. Postojeći Booking retci koji su prije imali
/// PaymentMethod=Package su migrirani BEZ stvaranja lažnog Payment retka (vidi
/// Migration_2026_09_16_PaymentLedger).
/// </summary>
public enum PaymentMethod
{
    Cash,
    Card,
    BankTransfer,
    Other
}
