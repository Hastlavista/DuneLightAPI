using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Phase M1C — JEDINA semantika preklapanja rasporeda: poluotvoreni intervali [start, end). 09:00-10:00 i 10:00-11:00 su
/// susjedni (NE preklapaju se); 09:00-10:00 i 09:59-11:00 se preklapaju. Svi upiti i provjere zauzetosti (zaposlenik,
/// klijent, prostorija, batch provjere) koriste ovu formulu — u memoriji <see cref="Overlaps"/>, u EF upitima
/// <see cref="SegmentOverlaps"/> (ista nejednakost, prevodi se u SQL).
/// </summary>
public static class SchedulingInterval
{
    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>Ista formula nad lokalnim vremenom dana (predlošci slotova, available-slots unutar jednog lokalnog dana).</summary>
    public static bool Overlaps(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd) =>
        aStart < bEnd && bStart < aEnd;

    /// <summary>Segment čiji se [PlannedStart, PlannedEnd) preklapa s [start, end).</summary>
    public static Expression<Func<AppointmentSegment, bool>> SegmentOverlaps(DateTimeOffset start, DateTimeOffset end) =>
        s => s.PlannedStart < end && start < s.PlannedEnd;
}

/// <summary>
/// Phase M1C — "rezervira li SEGMENT svoj izvršni slot?" (zaposlenik danas; prostorija i resursi u M1D). Odluka je po
/// SEGMENTU, nikad samo po agregatnom Appointment.Status (Closed termin može imati segmente s različitom poviješću):
/// <list type="number">
/// <item>termin NIJE eksplicitno otkazan → segment rezervira slot (i s nula sudjelovanja i kad su svi klijenti pojedinačno
/// otkazali — sesija i dalje postoji i može primiti novi Booking/walk-in; M1A.1: otkazivanje zadnjeg klijenta ne oslobađa
/// zaposlenika);</item>
/// <item>termin JE eksplicitno otkazan → segment ostaje povijesno zauzet samo ako ima izvršen/razriješen ishod
/// (Completed ili NoShow sudjelovanje); inače više ne zauzima.</item>
/// </list>
/// Zauzetost KLIJENTA je zasebna (sudjelovanje, <see cref="ParticipationOccupancy"/>).
/// </summary>
public static class SegmentOccupancy
{
    /// <summary>Za EF upite (translatable).</summary>
    public static readonly Expression<Func<AppointmentSegment, bool>> ReservesSlot =
        s => s.Appointment.CancelledAt == null ||
             s.Participations.Any(p => p.Status == ParticipationStatus.Completed || p.Status == ParticipationStatus.NoShow);

    /// <summary>U memoriji, nad učitanim terminom i sudjelovanjima segmenta.</summary>
    public static bool Reserves(Appointment appointment, AppointmentSegment segment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(segment);
        return !appointment.IsExplicitlyCancelled ||
               appointment.Bookings.SelectMany(b => b.Participations)
                   .Any(p => p.AppointmentSegmentId == segment.Id &&
                             (p.Status == ParticipationStatus.Completed || p.Status == ParticipationStatus.NoShow));
    }
}

/// <summary>
/// Phase M1C — ciljno stanje JEDNOG segmenta za provjeru tvrdih invarijanti: raspon, dodijeljeni zaposlenici i klijenti čije
/// bi sudjelovanje na njemu zauzimalo raspored. <see cref="SegmentId"/> je postojeći segment koji se mijenja (isključuje se
/// SAMO on iz vlastite provjere) ili null za novi segment.
/// </summary>
public sealed record SegmentClaim(
    Guid? SegmentId,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd,
    IReadOnlyCollection<Guid> EmployeeIds,
    IReadOnlyCollection<Guid> ClientIds)
{
    public bool Overlaps(SegmentClaim other) =>
        SchedulingInterval.Overlaps(PlannedStart, PlannedEnd, other.PlannedStart, other.PlannedEnd);

    /// <summary>Novi segment iz plana konstrukcijske jezgre (sudionici plana nastaju Confirmed — zauzimaju raspored).</summary>
    public static SegmentClaim ForNew(SegmentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new SegmentClaim(
            null, plan.PlannedStart, plan.PlannedEnd, plan.EmployeeIds.Distinct().ToList(),
            plan.Participants.Select(p => p.ClientId).Distinct().ToList());
    }
}

/// <summary>
/// Phase M1C — tvrde invarijante rasporeda (bez override-a, neovisno o poslovnici, usluzi, terminu i podrijetlu):
/// (1) isti zaposlenik ne smije biti dodijeljen dvama preklapajućim segmentima koji zauzimaju slot;
/// (2) isti klijent ne smije imati dva preklapajuća sudjelovanja koja zauzimaju raspored.
/// Ova klasa provjerava CILJNO stanje u memoriji (sudari IZMEĐU predloženih segmenata — novi sestrinski segment još ne
/// postoji u bazi); postojeće stanje provjerava ISchedulingOccupancyHandler pod zaključanim subjektima (vidi
/// <see cref="SchedulingLockOrder"/>).
/// </summary>
public static class SchedulingConflicts
{
    public const string EmployeeConflictMessage = "Trener već ima termin u ovom vremenskom razdoblju.";
    public const string ClientConflictMessage = "Klijent je već zakazan u vremenskom razdoblju ovog termina.";

    public static string ClientMessage(string clientLabel) => clientLabel == null
        ? ClientConflictMessage
        : $"Klijent {clientLabel} je već zakazan u ovom vremenskom razdoblju.";

    /// <summary>Sudari između segmenata istog ciljnog stanja: prvo zaposlenici, zatim klijenti.</summary>
    public static void EnsureTargetStateConsistent(IReadOnlyList<SegmentClaim> claims, Func<Guid, string> clientLabel = null)
    {
        ArgumentNullException.ThrowIfNull(claims);
        foreach (SegmentClaim claim in claims)
            if (claim.PlannedEnd <= claim.PlannedStart)
                throw new InvalidAppointmentSegmentStateException("Kraj segmenta mora biti nakon početka.");

        for (int i = 0; i < claims.Count; i++)
        for (int j = i + 1; j < claims.Count; j++)
        {
            if (!claims[i].Overlaps(claims[j]))
                continue;
            if (claims[i].EmployeeIds.Intersect(claims[j].EmployeeIds).Any())
                throw new BusinessRuleException(ErrorCodes.AppointmentOverlap,
                    "Isti zaposlenik ne može biti dodijeljen preklapajućim segmentima.");
        }

        for (int i = 0; i < claims.Count; i++)
        for (int j = i + 1; j < claims.Count; j++)
        {
            if (!claims[i].Overlaps(claims[j]))
                continue;
            Guid shared = claims[i].ClientIds.Intersect(claims[j].ClientIds).FirstOrDefault();
            if (shared != Guid.Empty)
                throw new BusinessRuleException(ErrorCodes.AppointmentOverlap,
                    clientLabel?.Invoke(shared) is { } label
                        ? $"Klijent {label} ne može sudjelovati u preklapajućim segmentima."
                        : "Isti klijent ne može sudjelovati u preklapajućim segmentima.");
        }
    }
}

/// <summary>
/// Phase M1C — JEDINO mjesto koje određuje redoslijed zaključavanja rasporeda. Tvrde invarijante štite se transakcijskim
/// advisory lockom PO SUBJEKTU (zaposlenik, klijent): svaka transakcija koja stvara ili proširuje zauzetost zaposlenika/
/// klijenta najprije zaključa te subjekte, tek ONDA (unutar iste transakcije, READ COMMITTED: svaka naredba vidi sve
/// commitano prije nje) provjerava postojeće preklapanje i piše. Dvije transakcije za istog subjekta se time serijaliziraju:
/// druga čeka na lock do commita/rollbacka prve i tek tada čita — vidi njezin commitani segment/sudjelovanje i odbija sudar.
/// (Zaključavanje samo trenutno sudarajućih redaka NE bi bilo dovoljno — novi redak još ne postoji: phantom insert.)
/// Nema zaključavanja cijele organizacije/poslovnice: različiti zaposlenici/klijenti se ne serijaliziraju.
///
/// GLOBALNI REDOSLIJED (bez ciklusa):
/// <list type="number">
/// <item>subjekti rasporeda — svi zaposlenici, pa svi klijenti (ključevi uzlazno; vrsta je u gornjem bajtu ključa pa je
/// "zaposlenici prije klijenata" posljedica istog sortiranja) — UVIJEK prvi lock transakcije koja ih treba;</item>
/// <item>advisory lock slota grupe (generiranje occurrencea);</item>
/// <item>Appointment redak (FOR UPDATE);</item>
/// <item>sudjelovanja (FOR UPDATE, po Id-u);</item>
/// <item>ClientPackage redak.</item>
/// </list>
/// Iznimka: promocija s liste čekanja izvršava se UNUTAR transakcije koja već drži Appointment lock — zato subjekt (klijent)
/// zaključava SAMO neblokirajuće (pg_try_advisory_xact_lock): nikad ne čeka, pa ne može zatvoriti ciklus; ako lock nije
/// odmah dostupan, promocija tog (i daljnjih, FIFO) čekatelja se odgađa za sljedeću priliku.
/// </summary>
public static class SchedulingLockOrder
{
    private const byte EmployeeTag = 0x41;
    private const byte ClientTag = 0x42;

    /// <summary>Uzlazno sortirani, deduplicirani ključevi: zaposlenici (0x41…) pa klijenti (0x42…). Sudar dvaju ključeva
    /// iste vrste samo spaja lock (lock je jedna vrijednost) — redoslijed je totalni nad stvarnim vrijednostima locka.</summary>
    public static IReadOnlyList<long> Keys(IEnumerable<Guid> employeeIds, IEnumerable<Guid> clientIds) =>
        (employeeIds ?? Enumerable.Empty<Guid>()).Select(EmployeeKey)
            .Concat((clientIds ?? Enumerable.Empty<Guid>()).Select(ClientKey))
            .Distinct()
            .OrderBy(k => k)
            .ToList();

    public static long EmployeeKey(Guid employeeId) => Key(EmployeeTag, employeeId);

    public static long ClientKey(Guid clientId) => Key(ClientTag, clientId);

    /// <summary>Gornji bajt = vrsta subjekta, donjih 56 bita = FNV-1a nad bajtovima Guida (deterministično, neovisno o
    /// procesu/platformi).</summary>
    private static long Key(byte tag, Guid id)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in id.ToByteArray())
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return (long)(((ulong)tag << 56) | (hash & 0x00FF_FFFF_FFFF_FFFFUL));
    }
}
