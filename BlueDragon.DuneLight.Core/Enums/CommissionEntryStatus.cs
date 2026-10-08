namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Earned → Reversed na istom retku (ReversedAt/ReversedBy/ReversalReason); retci se NIKAD ne brišu. Putevi reverzije:
/// korekcija individualnog completiona (BookingService.ApplyIndividualCompletionCorrection), P2 (2F) Q38 naknada koja više nije
/// plaćena / je oproštena, i korekcija korisnika provizije na prodaju (Q50, reverzija + nova provizija). Grupni completion nema
/// put natrag. Izvještavanje je po događajima: zarada u razdoblju EarnedAt, storno kao negativan iznos u razdoblju ReversedAt.
/// </summary>
public enum CommissionEntryStatus
{
    Earned,
    Reversed
}
