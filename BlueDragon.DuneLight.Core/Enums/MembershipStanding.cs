namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (Q15) — izvedeno stanje duga članstva iz NAJSTARIJEG nekonačnog zaduženja: Current (nema dospjelog duga),
/// InGrace (dug unutar grace perioda), Delinquent (grace istekao; PartiallyPaid se tretira kao neplaćeno).</summary>
public enum MembershipStanding
{
    Current,
    InGrace,
    Delinquent
}
