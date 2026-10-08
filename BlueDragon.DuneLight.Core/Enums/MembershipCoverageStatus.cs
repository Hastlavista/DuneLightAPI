namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2D) — stanje pokrića sudjelovanja članarinom (projekcija ledgera). Covered = aktivan claim (i kad je kredit
/// zadržan kao kazna kasnog otkaza/izostanka); Released = claim vraćen jer sudjelovanje više ne koristi uslugu (otkaz na vrijeme,
/// poslovni/sustavski otkaz, otpis); NotCovered = članarina ne pokriva (razlog); PendingEvaluation = termin iza horizonta
/// (tekući + sljedeći period, Q27) — bez duga i bez naplate dok se ne evaluira. Sudjelovanje bez projekcije = klijent nema
/// članarinu relevantnu za termin (ponašanje kao prije P2).</summary>
public enum MembershipCoverageStatus
{
    Covered,
    NotCovered,
    PendingEvaluation,
    Released
}
