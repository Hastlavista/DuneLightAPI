namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q14) — na koga se odnosi nova verzija plana: samo nove prodaje (default) ili i postojeća članstva (od
/// prve obnove nakon roka najave; strogo povoljne izmjene od sljedeće obnove, Q48).</summary>
public enum MembershipPlanApplyTo
{
    NewSalesOnly,
    NewSalesAndExisting
}
