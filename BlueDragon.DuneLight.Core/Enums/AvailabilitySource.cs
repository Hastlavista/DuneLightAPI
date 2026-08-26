namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Objašnjava UI-ju zašto AvailabilityDto za taj dan izgleda kako izgleda.</summary>
public enum AvailabilitySource
{
    Template,
    Override,
    Absence,
    Holiday,
    None
}

/// <summary>RosterDayCellDto/RosterPlannedDayDto (team-monthly/personal pregled) — stvarni zapisi uvijek imaju
/// prednost; Assumed = prošli/današnji dan bez zapisa gdje predložak razrješava radne intervale (tretira se kao
/// odrađeno, ulazi u TotalWorkHours, FE ga prikazuje identično kao Actual); Planned = budući dan bez zapisa s
/// predloškom (ne ulazi u zbroj).</summary>
public enum RosterCellSource
{
    Actual,
    Assumed,
    Planned,
    None
}