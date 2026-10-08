namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>P2 (2F, Q42) — ishod jednokratne evaluacije provizije na prvu prodaju članarine. NoRecipient (nije bilo korisnika)
/// dopušta naknadnu dodjelu korisnika uz commissions.manage + razlog; NoRule i ZeroBase su konačni.</summary>
public enum MembershipFirstSaleCommissionOutcome
{
    Earned,
    NoRecipient,
    NoRule,
    ZeroBase
}
