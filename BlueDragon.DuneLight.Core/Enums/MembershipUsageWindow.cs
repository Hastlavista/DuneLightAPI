namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P2 (Q13/Q17) — prozor limita korištenja članarine. Period = krediti jednog perioda članarine; ostali prozori su
/// kalendarski (dan, tjedan pon–ned, kalendarski mjesec, kvartal) u zoni poslovnice termina. Više limita se kombinira kao AND.
/// </summary>
public enum MembershipUsageWindow
{
    Period,
    Day,
    Week,
    Month,
    Quarter
}
