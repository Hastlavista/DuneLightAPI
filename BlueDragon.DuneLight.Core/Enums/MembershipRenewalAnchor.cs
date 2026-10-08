namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P2 (Q3) — način obnove plana članarine.
/// PurchaseDate: period traje od datuma početka do istog dana sljedećeg intervala (dan koji ne postoji u mjesecu → zadnji dan
/// mjeseca, pa povratak na izvorni dan); puni iznos, bez proporcije.
/// CalendarMonth: periodi su kalendarski mjeseci; prvi period traje do kraja tekućeg mjeseca uz puni iznos i pune kredite.
/// Samo uz MembershipBillingInterval.Monthly.
/// </summary>
public enum MembershipRenewalAnchor
{
    PurchaseDate,
    CalendarMonth
}
