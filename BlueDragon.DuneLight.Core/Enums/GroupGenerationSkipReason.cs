namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// K1-7 — zašto generiranje grupnih termina nešto nije generiralo (jedan popis za frontend). CompanyInactive: cijela grupa
/// (poslovnica grupe je neaktivna, bug a); CompanyHoliday: jedan datum slota (praznik poslovnice bez potvrde, bug d).
/// Već generirani occurrence nije na popisu (samo u SkippedCount).
/// </summary>
public enum GroupGenerationSkipReason
{
    CompanyInactive,
    CompanyHoliday
}
