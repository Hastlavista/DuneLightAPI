namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2F, Q38) — provizija za kasni otkaz / izostanak individualne sesije: Never (default) ili WhenFeePaid (nastaje kad
/// je P1 naknada plaćena u cijelosti, osnovica = naknada; storno uplate ili oprost je poništava).</summary>
public enum CommissionLateCancellationMode
{
    Never,
    WhenFeePaid
}
