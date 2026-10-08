namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q31) — dimenzija P1 politike po događaju (kasni otkaz, izostanak) za sesiju pokrivenu članarinom.
/// ForfeitCredit (default): claim ostaje (kredit i mjesta u limitima potrošeni) umjesto naknade; na planu bez kredita perioda
/// naplaćuje se P1 naknada (Q26). ReturnCreditChargeFee: storno cijelog claima + P1 naknada.</summary>
public enum CancellationMembershipAction
{
    ForfeitCredit,
    ReturnCreditChargeFee
}
