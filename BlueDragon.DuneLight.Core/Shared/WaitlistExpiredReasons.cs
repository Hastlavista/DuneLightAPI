namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedini izvor istine za WaitlistEntry.ExpiredReason vrijednosti — isti ugovor kao ErrorCodes/WarningCodes
/// (stabilan prema frontendu, ne mijenjati postojeće vrijednosti). Popunjava se isključivo kad WaitlistEntry
/// pređe u Expired (nikad za Cancelled — to je eksplicitna odluka klijenta/osoblja, ne sustava).
/// </summary>
public static class WaitlistExpiredReasons
{
    public const string ClientInactive = "CLIENT_INACTIVE";
    public const string ClientAnonymized = "CLIENT_ANONYMIZED";
    public const string ClientScheduleConflict = "CLIENT_SCHEDULE_CONFLICT";
    public const string AppointmentNoLongerAvailable = "APPOINTMENT_NO_LONGER_AVAILABLE";
    public const string AppointmentCancelled = "APPOINTMENT_CANCELLED";
    public const string AppointmentCompleted = "APPOINTMENT_COMPLETED";
}
