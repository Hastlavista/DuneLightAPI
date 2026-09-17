namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Namjerno minimalan — nema Sent/Delivered/Failed jer NE postoji provider koji stvarno isporučuje (vidi spec
/// section 26/29). Pending = "komunikacija bi se trebala dogoditi". Cancelled = ili je izvorni poslovni događaj
/// naknadno administrativno ispravljen (npr. NoShow -> Confirmed, vidi BookingNoShowNotificationHandler), ili
/// primatelj (Client) više ne smije primiti komunikaciju (anonimiziran, vidi handlere).
/// </summary>
public enum NotificationStatus
{
    Pending,
    Cancelled
}
