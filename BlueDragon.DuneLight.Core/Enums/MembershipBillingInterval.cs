namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P2 — interval naplate i obnove plana članarine. Kalendarski način obnove (MembershipRenewalAnchor.CalendarMonth) je
/// dopušten samo uz Monthly; godišnji planovi se uvijek obnavljaju od datuma kupnje (P2 decision record, 2A).
/// </summary>
public enum MembershipBillingInterval
{
    Monthly,
    Yearly
}
