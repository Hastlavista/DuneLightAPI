namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Sistemski ključevi GrantGroup-a koje aplikacija sama kreira (grant_groups.system_key, ADR-0023). Ključ je stabilni
/// identitet grupe neovisan o nazivu — nikad se ne koristi za autorizaciju.
/// </summary>
public static class SystemGrantGroups
{
    /// <summary>Inicijalna Admin grupa koju registracija organizacije kreira sa SVIM grantovima iz Grants.Catalog i
    /// dodjeljuje prvom korisniku. Grupa je organizacijska (smije se preimenovati, mijenjati i obrisati); migracija koja
    /// uvodi novi grant ga eksplicitno dodaje svim grupama s ovim ključem.</summary>
    public const string Admin = "admin";

    /// <summary>Početni naziv inicijalne Admin grupe — samo prikaz, ništa ne ovisi o njemu.</summary>
    public const string AdminDisplayName = "Admin";
}
