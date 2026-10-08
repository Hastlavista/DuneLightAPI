namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q14, plan-change) — izvor zakazane promjene uvjeta od obnove: promjena plana koju je zatražio klijent
/// (ima prednost) ili izmjena plana prenesena na postojeća članstva.</summary>
public enum MembershipPendingChangeSource
{
    ClientPlanChange,
    PlanUpdate
}
