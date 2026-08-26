using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Kanonski popis tri default RosterType zapisa (Rad/Godišnji/Bolovanje) koje svaka organizacija treba imati
/// odmah nakon nastanka, isti obrazac kao DefaultGrantGroups. Jedini izvor istine za AuthService.Register
/// (nove organizacije) — postojeće organizacije se retroaktivno NE dopunjuju.
/// </summary>
public static class DefaultRosterTypes
{
    public const string WorkName = "Rad";
    public const string AnnualLeaveName = "Godišnji";
    public const string SickLeaveName = "Bolovanje";

    public static readonly IReadOnlyList<DefaultRosterTypeDefinition> All = new List<DefaultRosterTypeDefinition>
    {
        new DefaultRosterTypeDefinition(WorkName, "#3B82F6", countsAsWork: true, isAbsence: false, requiresTime: true, deductsFromLeaveFund: false, sortOrder: 1),
        new DefaultRosterTypeDefinition(AnnualLeaveName, "#22C55E", countsAsWork: false, isAbsence: true, requiresTime: false, deductsFromLeaveFund: true, sortOrder: 2),
        new DefaultRosterTypeDefinition(SickLeaveName, "#F97316", countsAsWork: false, isAbsence: true, requiresTime: false, deductsFromLeaveFund: false, sortOrder: 3)
    };
}

public class DefaultRosterTypeDefinition
{
    public DefaultRosterTypeDefinition(string name, string colorHex, bool countsAsWork, bool isAbsence, bool requiresTime, bool deductsFromLeaveFund, int sortOrder)
    {
        Name = name;
        ColorHex = colorHex;
        CountsAsWork = countsAsWork;
        IsAbsence = isAbsence;
        RequiresTime = requiresTime;
        DeductsFromLeaveFund = deductsFromLeaveFund;
        SortOrder = sortOrder;
    }

    public string Name { get; }
    public string ColorHex { get; }
    public bool CountsAsWork { get; }
    public bool IsAbsence { get; }
    public bool RequiresTime { get; }
    public bool DeductsFromLeaveFund { get; }
    public int SortOrder { get; }
}
