namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// P2 (Q29) — eksplicitni opseg poslovnica plana članarine. AllCompanies vrijedi u svim poslovnicama, uključujući buduće;
/// SelectedCompanies traži barem jednu poslovnicu. Prazna lista NIKAD ne znači "sve".
/// </summary>
public enum MembershipCompanyScope
{
    AllCompanies,
    SelectedCompanies
}
