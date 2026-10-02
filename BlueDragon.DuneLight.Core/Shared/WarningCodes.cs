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
    /// <summary>UMIROVLJENO (Phase M1G — više se ne emitira): close-out višesegmentnog occurrencea sada računa grupnu
    /// proviziju po SEGMENTU i zaposleniku. Vrijednost ostaje rezervirana (stabilan ugovor prema frontendu).</summary>
    public const string GroupCommissionNotSupportedForMultiSegment = "GROUP_COMMISSION_NOT_SUPPORTED_FOR_MULTI_SEGMENT";

    /// <summary>Phase M1G — close-out grupne sesije: zaposlenik segmenta ima aktivno pravilo provizije koje grupna sesija ne
    /// podržava (Percentage — za grupnu sesiju ne postoji osnovica; ne izmišlja se). Unos se NE stvara; details:
    /// WarningGroupCommissionRuleDetails.</summary>
    public const string GroupCommissionRuleNotSupported = "GROUP_COMMISSION_RULE_NOT_SUPPORTED";

    // Roster
    public const string RosterEntryOverlap = "ROSTER_ENTRY_OVERLAP";

    // Employees
    public const string EmployeeHasFutureAppointments = "EMPLOYEE_HAS_FUTURE_APPOINTMENTS";
}
