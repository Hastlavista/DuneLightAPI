using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Čisti izračuni datuma za fond godišnjeg odmora iz EmployeeLeaveSettings (mjesec+dan bez godine) — isti
/// stil kao PackageExpiryCalculator. 29.2. na neprijestupnu godinu klipa se na 28.2. (SafeDate).
/// </summary>
public static class LeaveFundYearCalculator
{
    /// <summary>Kojoj obračunskoj godini (FundYear) pripada dani datum — prije datuma obnove u toj kalendarskoj godini pripada prethodnoj obračunskoj godini.</summary>
    public static int ResolveFundYear(EmployeeLeaveSettings settings, DateTimeOffset date)
    {
        DateTime renewalThisCalendarYear = SafeDate(date.Year, settings.RenewalMonth, settings.RenewalDay);
        return date.Date >= renewalThisCalendarYear ? date.Year : date.Year - 1;
    }

    public static DateTimeOffset ResolveOpenedAt(EmployeeLeaveSettings settings, int fundYear)
    {
        return new DateTimeOffset(SafeDate(fundYear, settings.RenewalMonth, settings.RenewalDay), TimeSpan.Zero);
    }

    /// <summary>Fond otvoren u fundYear ističe (za prijenos) na CarryoverExpiry datum SLJEDEĆE kalendarske godine (npr. fond 2026 s prijenosom do 30.6. ističe 30.6.2027.).</summary>
    public static DateTimeOffset ResolveExpiresAt(EmployeeLeaveSettings settings, int fundYear)
    {
        return new DateTimeOffset(SafeDate(fundYear + 1, settings.CarryoverExpiryMonth, settings.CarryoverExpiryDay), TimeSpan.Zero);
    }

    private static DateTime SafeDate(int year, int month, int day)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, Math.Min(day, daysInMonth));
    }
}
