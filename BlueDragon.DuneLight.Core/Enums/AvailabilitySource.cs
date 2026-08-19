namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>Objašnjava UI-ju zašto AvailabilityDto za taj dan izgleda kako izgleda.</summary>
public enum AvailabilitySource
{
    Template,
    Override,
    Absence,
    None
}

/// <summary>RosterDayCellDto/RosterPlannedDayDto (team-monthly/personal pregled) — stvarni zapisi uvijek imaju
/// prednost; Planned se prikazuje samo za dane bez stvarnog zapisa kod kojih postoji WorkingHoursTemplate.</summary>
public enum RosterCellSource
{
    Actual,
    Planned,
    None
}