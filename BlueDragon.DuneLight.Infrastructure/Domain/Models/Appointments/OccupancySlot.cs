using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Zauzetost rasporeda koju stvara JEDAN SEGMENT — jedini oblik u kojem provjere preklapanja (zaposlenik, prostorija,
/// klijent) i available-slots vide postojeće stanje (vidi ISchedulingOccupancyHandler). Nije EF entitet i nema tablicu:
/// projekcija nad AppointmentSegment i sudjelovanjima tog segmenta.
///
/// Phase M1C: izvršni identitet je <see cref="SegmentId"/> (AppointmentId je samo kontekst — sestrinski segmenti istog
/// termina su zasebne zauzetosti). Start/End = PlannedStart/PlannedEnd segmenta (UTC instanti, poluotvoreno [Start, End)).
/// EmployeeIds = zaposlenici segmenta (samo za segmente koji rezerviraju slot — SegmentOccupancy). ActiveClientIds =
/// klijenti čije sudjelovanje NA OVOM SEGMENTU zauzima raspored (ParticipationOccupancy: Confirmed/Completed).
/// </summary>
public sealed record OccupancySlot(
    Guid OrganizationId,
    Guid CompanyId,
    Guid AppointmentId,
    Guid SegmentId,
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<Guid> EmployeeIds,
    Guid? RoomId,
    IReadOnlyList<Guid> ActiveClientIds)
{
    /// <summary>Centralno pravilo (<see cref="SchedulingInterval"/>): susjedni intervali NISU sudar.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => SchedulingInterval.Overlaps(Start, End, start, end);
}
