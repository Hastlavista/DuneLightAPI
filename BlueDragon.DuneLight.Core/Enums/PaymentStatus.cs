namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Namjerno minimalan skup — bez Pending/Processing/Failed (to su brige vanjskog platnog procesora,
/// odgođeno za kasnije integracije, vidi Payment.cs). Refunded je izostavljen jer stvarni povrat novca
/// (refund workflow) nije dio ovog zahvata — administrativna ispravka pogreške ide kroz Voided (vidi
/// domensku napomenu na Payment.cs za razliku Void naspram Refund).
/// </summary>
public enum PaymentStatus
{
    Completed,
    Voided
}
