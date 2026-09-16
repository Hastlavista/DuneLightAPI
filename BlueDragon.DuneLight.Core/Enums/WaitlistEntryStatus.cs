namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>
/// Stanje jednog retka liste čekanja za KONKRETAN grupni Appointment occurrence (ne za Group definiciju kao
/// takvu — vidi WaitlistEntry.cs). Waiting je jedino ne-terminalno stanje. Nema ordinal/poziciju kao trajno
/// polje — FIFO redoslijed se izvodi iz (JoinedAt, Id), vidi IWaitlistService.
/// </summary>
public enum WaitlistEntryStatus
{
    Waiting,
    Promoted,
    Cancelled,
    Expired
}
