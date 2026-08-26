using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Katalog fiksnih (nepomičnih) državnih praznika, po ISO 3166-1 alpha-2 kodu države (Company.Country) —
/// isti obrazac kao DefaultRosterTypes. Koristi ga CompanyHolidayService.Generate za auto-generiranje po
/// godini i poslovnici. Pomični datumi (Uskrs, Uskrsni ponedjeljak, Tijelovo) namjerno NISU ovdje — dodaju
/// se ručno, pojedinačno, po poslovnici. Trenutno je definirana samo Hrvatska ("HR") — druge države se
/// dodaju kao novi case u For(), bez izmjene pozivatelja.
/// </summary>
public static class DefaultCompanyHolidays
{
    private static readonly IReadOnlyList<DefaultCompanyHolidayDefinition> Croatia = new List<DefaultCompanyHolidayDefinition>
    {
        new DefaultCompanyHolidayDefinition("Nova godina", 1, 1),
        new DefaultCompanyHolidayDefinition("Sveta tri kralja", 1, 6),
        new DefaultCompanyHolidayDefinition("Praznik rada", 5, 1),
        new DefaultCompanyHolidayDefinition("Dan državnosti", 5, 30),
        new DefaultCompanyHolidayDefinition("Dan antifašističke borbe", 6, 22),
        new DefaultCompanyHolidayDefinition("Dan pobjede i domovinske zahvalnosti", 8, 5),
        new DefaultCompanyHolidayDefinition("Velika Gospa", 8, 15),
        new DefaultCompanyHolidayDefinition("Dan svih svetih", 11, 1),
        new DefaultCompanyHolidayDefinition("Dan sjećanja", 11, 18),
        new DefaultCompanyHolidayDefinition("Božić", 12, 25),
        new DefaultCompanyHolidayDefinition("Sveti Stjepan", 12, 26)
    };

    private static readonly IReadOnlyList<DefaultCompanyHolidayDefinition> Empty = new List<DefaultCompanyHolidayDefinition>();

    /// <summary>Fiksni praznici za zadanu državu, ili prazan popis ako država nema definiran katalog
    /// (CompanyHolidayService.Generate to prijavljuje kao grešku umjesto tihog no-op-a).</summary>
    public static IReadOnlyList<DefaultCompanyHolidayDefinition> For(string countryCode)
    {
        if (string.Equals(countryCode, "HR", StringComparison.OrdinalIgnoreCase))
            return Croatia;

        return Empty;
    }
}

public class DefaultCompanyHolidayDefinition
{
    public DefaultCompanyHolidayDefinition(string name, int month, int day)
    {
        Name = name;
        Month = month;
        Day = day;
    }

    public string Name { get; }
    public int Month { get; }
    public int Day { get; }
}
