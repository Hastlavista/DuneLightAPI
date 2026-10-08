namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q15) — ponašanje kod duga nakon grace perioda (postavka organizacije, default StopCovering).</summary>
public enum MembershipDebtBehavior
{
    KeepCovering,
    StopCovering,
    BlockBooking
}
