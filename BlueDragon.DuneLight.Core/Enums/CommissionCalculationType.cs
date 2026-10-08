namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Izračun pravila provizije — vidi CommissionRule.Value. Percentage zahtijeva 0-100, Fixed zahtijeva samo &gt;= 0 (vidi
/// CommissionService validaciju). P2 (2F, Vagaro): None ("Bez provizije") je izričit izbor za pravilo USLUGE — zaposlenik za tu
/// uslugu ne dobiva ništa, a opće pravilo se ne primjenjuje (nula nikad ne znači "vrati se na drugo pravilo"). Zapis provizije
/// nikad nije None (tada provizija ne nastaje). Razine po prometu (tiered) su faza Payroll.
/// </summary>
public enum CommissionCalculationType
{
    Percentage,
    Fixed,
    None
}
