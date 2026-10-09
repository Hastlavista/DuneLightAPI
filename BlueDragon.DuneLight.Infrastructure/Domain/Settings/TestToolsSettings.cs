namespace BlueDragon.DuneLight.Infrastructure.Domain.Settings;

/// <summary>
/// T1 — jedan prekidač za PRIVREMENE testne alate (simulirani pomak sata i seed iz Managementa). Uključuje se samo izričito
/// (<c>TestTools:Enabled = true</c>), neovisno o imenu okruženja; u okruženju Production uključena postavka zaustavlja pokretanje.
/// Isključeno: rute testnih alata ne postoje, poslovni sat = stvarni sat. Uklanja se prije go-livea.
/// </summary>
public class TestToolsSettings
{
    public bool Enabled { get; set; }

    /// <summary>Gornja granica jednog skoka sata u danima (T1-Q5).</summary>
    public const int MaxAdvanceDays = 400;
}
