using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Jedinstven ugovor za sva neblokirajuća upozorenja koja API vraća uz uspješan odgovor (create/update i sl.).
/// Isti obrazac kao ErrorResponse/ErrorDetail — Code je stabilan ugovor prema frontendu (i18n se veže na njega),
/// Details nosi samo strukturirane podatke (npr. datume, imena, brojeve), nikad gotov tekst na hrvatskom.
/// </summary>
public class WarningDto
{
    public WarningDto()
    {
    }

    public WarningDto(string code, object details = null)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; set; }
    public object Details { get; set; }
}

/// <summary>WarningCodes.OutsideWorkingHours details za definiciju grupnog slota (Group Create/Update/AddSlot/
/// UpdateSlot) — nema konkretnog datuma, samo dan-u-tjednu + vrijeme.</summary>
public class WarningSlotDetails
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
}

/// <summary>WarningCodes.GroupCapacityExceeded details.</summary>
public class WarningGroupCapacityDetails
{
    public int Capacity { get; set; }
    public int ActiveMemberCount { get; set; }
}

/// <summary>WarningCodes.GroupAppointmentUnresolvedBookings details — Bookinzi koji su ostali Confirmed
/// (nerazrješeni) u trenutku zatvaranja grupnog termina. ClientId nije PII u ovom ugovoru (isti nivo detalja
/// kao ostali booking payloadi u ovom API-ju).</summary>
public class WarningUnresolvedBookingsDetails
{
    public List<Guid> ClientIds { get; set; } = new();
}

/// <summary>WarningCodes.RosterEntryOverlap details — postojeći zapis s kojim se preklapa.</summary>
public class WarningRosterOverlapDetails
{
    public string RosterTypeName { get; set; }
    public bool IsAbsence { get; set; }
    public DateTimeOffset DateFrom { get; set; }
    public DateTimeOffset? DateTo { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
}
