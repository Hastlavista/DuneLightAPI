using System;
using FluentMigrator;

namespace BlueDragon.DuneLight.DatabaseMigration.Extensions;

public enum Developer
{
    SilvioHabazin = 0
}

/// <summary>
/// Must use this one! Serves to enable independent database development for multiple developers/teams. Will generate version
/// numbers containing info on the date and author of migration.
/// </summary>
public class DeveloperMigrationAttribute : MigrationAttribute
{
    public DeveloperMigrationAttribute(
        int creationYear,
        int creationMonth,
        int creationDay,
        Developer developer,
        int additionalOrderInfo
    ) : base(Calculate(creationYear, creationMonth, creationDay, developer, additionalOrderInfo))
    {
    }

    /// <summary>
    /// Format: "yyyyMMddAAAAoo", where "AAAA" is the author's corresponding int-value, and "oo" is the additional
    /// order info (which is validated, BTW, to make sure you don't pass in something &gt;99 or &lt;0).
    /// </summary>
    private static long Calculate(int creationYear, int creationMonth, int creationDay, Developer author, int additionalOrderInfo)
    {
        if (additionalOrderInfo > 99 || additionalOrderInfo < 0)
            throw new ArgumentException();

        return creationYear * 10000000000 +
               creationMonth * 100000000 +
               creationDay * 1000000 +
               ((int)author) * 100 +
               additionalOrderInfo;
    }
}
