using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Čisti izračuni datuma za fond godišnjeg odmora iz EmployeeLeaveSettings (mjesec+dan bez godine) — isti
/// stil kao PackageExpiryCalculator. 29.2. na neprijestupnu godinu klipa se na 28.2. (SafeDate).
/// T1-7: sve vrijednosti su kalendarski dani (DateOnly); "danas" određuje pozivatelj kroz kalendar organizacije.
/// </summary>
public static class LeaveFundYearCalculator
{
    /// <summary>Kojoj obračunskoj godini (FundYear) pripada dani kalendarski datum — prije datuma obnove u toj kalendarskoj
    /// godini pripada prethodnoj obračunskoj godini.</summary>
    public static int ResolveFundYear(EmployeeLeaveSettings settings, DateOnly date)
    {
        DateOnly renewalThisCalendarYear = SafeDate(date.Year, settings.RenewalMonth, settings.RenewalDay);
        return date >= renewalThisCalendarYear ? date.Year : date.Year - 1;
    }

    public static DateOnly ResolveOpenedAt(EmployeeLeaveSettings settings, int fundYear)
    {
        return SafeDate(fundYear, settings.RenewalMonth, settings.RenewalDay);
    }

    /// <summary>Fond otvoren u fundYear ističe (za prijenos) na CarryoverExpiry datum SLJEDEĆE kalendarske godine (npr. fond 2026
    /// s prijenosom do 30.6. ističe 30.6.2027.). Zadnji dan na koji se fond još smije trošiti (uključivo).</summary>
    public static DateOnly ResolveExpiresAt(EmployeeLeaveSettings settings, int fundYear)
    {
        return SafeDate(fundYear + 1, settings.CarryoverExpiryMonth, settings.CarryoverExpiryDay);
    }

    /// <summary>T1-10 (uključiv kraj, ADR-0035): fond vrijedi i na dan ExpiresAt; istekao je tek kad je danas (kalendar
    /// organizacije) NAKON ExpiresAt.</summary>
    public static bool IsExpired(LeaveFund fund, DateOnly today) => today > fund.ExpiresAt;

    private static DateOnly SafeDate(int year, int month, int day)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, daysInMonth));
    }
}
