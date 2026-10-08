namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q53) — razrješenje preskočenog člana grupe u dugu nakon što dug prestane: dodan u termin, termin pun (ostaje na
/// popisu recepciji), sudar rasporeda klijenta, već sudjeluje (npr. ručno dodan uz grant), ili više nije primjenjivo (termin
/// otkazan ili prošao, klijent više nije član).</summary>
public enum GroupMembershipSkipResolution
{
    Added,
    CapacityFull,
    Conflict,
    AlreadyParticipating,
    NotApplicable
}
