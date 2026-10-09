namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 — koje je pravilo odredilo datum od kad otkaz djeluje (prikaz recepciji).</summary>
public enum MembershipEndEffectiveReason
{
    EndOfPeriod,
    NoticePeriod,
    MinimumCommitment,
    BeforeStart,

    /// <summary>K1-8 — članstvo stoji zbog zatvorenih poslovnica: otkaz djeluje odmah, bez roka i obveze.</summary>
    CompanyClosure
}
