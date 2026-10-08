namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2D) — razlog trenutne odluke o pokriću (prikaz recepciji; nijedno pokriće nije tiho).</summary>
public enum MembershipCoverageReason
{
    /// <summary>Pokriveno: kredit i mjesta u limitima potrošeni.</summary>
    Claimed,
    /// <summary>Kasni otkaz / izostanak uz ForfeitCredit na planu s kreditima perioda: kredit propada umjesto P1 naknade (Q26).</summary>
    CreditForfeited,
    /// <summary>Kasni otkaz / izostanak na planu bez kredita perioda: mjesto u prozorima ostaje potrošeno, naplaćuje se P1 naknada (Q26).</summary>
    SlotUsedFeeCharged,
    ServiceNotCovered,
    CompanyNotCovered,
    Paused,
    /// <summary>Termin je nakon (zakazanog) kraja članstva (Q27).</summary>
    AfterMembershipEnd,
    /// <summary>Termin je prije početka članstva (npr. nakon promjene vremena).</summary>
    BeforeMembershipStart,
    /// <summary>Limit je iskorišten (koji: limit_window/limit_service_id); sesija ide na sljedeći izvor (Q4).</summary>
    LimitReached,
    /// <summary>Sudjelovanje je već (djelomično) plaćeno novcem — ostaje plaćeno, kredit se ne troši (2D).</summary>
    AlreadyPaid,
    /// <summary>Dug nakon grace perioda uz ponašanje StopCovering/BlockBooking (Q15).</summary>
    DebtNotCovered,
    /// <summary>Termin je iza horizonta pokrića (tekući + sljedeći period, Q27) — evaluira se kad period uđe u horizont.</summary>
    BeyondHorizon,
    CancelledOnTime,
    CancelledByBusiness,
    CancelledBySystem,
    /// <summary>Otpis posljedice politike vraća cijeli claim (Q31.3).</summary>
    PolicyWaived,
    /// <summary>ReturnCreditChargeFee: claim vraćen, naplaćuje se P1 naknada (Q31.2).</summary>
    CreditReturnedWithFee,
    /// <summary>Kasni otkaz / izostanak po politici bez naknade (bez izričitog ForfeitCredit, pregled 2D #8): claim vraćen, bez posljedice.</summary>
    CreditReturned,
    /// <summary>Članstvo je poništeno (Q24.4).</summary>
    MembershipVoided
}
