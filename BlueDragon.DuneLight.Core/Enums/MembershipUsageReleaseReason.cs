namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2D) — razlog storna claima (Release +1).</summary>
public enum MembershipUsageReleaseReason
{
    CancelledOnTime,
    CancelledByBusiness,
    CancelledBySystem,
    PolicyWaived,
    CreditReturnedWithFee,
    /// <summary>Politika bez naknade (pregled 2D #8): kasni otkaz / izostanak ne troši kredit.</summary>
    CreditReturned,
    Paused,
    AfterMembershipEnd,
    DebtNotCovered,
    /// <summary>Uvjeti članstva na datum termina više ne pokrivaju uslugu/poslovnicu (promjena plana).</summary>
    TermsChanged,
    Rescheduled,
    /// <summary>Netaknuto sudjelovanje se fizički briše (uređivanje termina).</summary>
    ParticipationRemoved,
    /// <summary>Poništena prodaja članarine (Q24.4, 2D): budući claimovi se vraćaju.</summary>
    MembershipVoided
}
