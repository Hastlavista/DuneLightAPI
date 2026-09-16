namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za sve "code" vrijednosti unutar WarningDto ({ "code", "details" }).
/// Isti ugovor kao ErrorCodes, ali za neblokirajuća upozorenja vraćena uz uspješan odgovor
/// (create/update i sl.) — stabilan ugovor prema frontendu (i18n se veže na code), ne mijenjati
/// postojeće vrijednosti, samo dodavati nove.
/// </summary>
public static class WarningCodes
{
    // Appointments i Groups — planiranje izvan pravila ne blokira spremanje, samo se prijavljuje.
    public const string EmployeeOnBreak = "EMPLOYEE_ON_BREAK";
    public const string EmployeeAbsent = "EMPLOYEE_ABSENT";

    /// <summary>Konkretan termin/instanca izvan radnog vremena zaposlenika ili poslovnice. Za definiciju
    /// grupnog slota (dan-u-tjednu + vrijeme, bez konkretnog datuma) nosi WarningSlotDetails u details.</summary>
    public const string OutsideWorkingHours = "OUTSIDE_WORKING_HOURS_WARNING";
    public const string CompanyClosedHoliday = "COMPANY_CLOSED_HOLIDAY";

    // Groups
    public const string GroupCapacityExceeded = "GROUP_CAPACITY_EXCEEDED";
    public const string GroupAppointmentUnresolvedBookings = "GROUP_APPOINTMENT_UNRESOLVED_BOOKINGS";

    // Roster
    public const string RosterEntryOverlap = "ROSTER_ENTRY_OVERLAP";

    // Employees
    public const string EmployeeHasFutureAppointments = "EMPLOYEE_HAS_FUTURE_APPOINTMENTS";
}
