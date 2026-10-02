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
/// Phase M1C/M1D — zauzetost koju upis TRAŽI za jedan segment: raspon, zaposlenici i klijenti čije bi sudjelovanje zauzimalo
/// raspored (tvrdo preklapanje), te osobe u prostoriji i količine resursa (tvrdi kapacitet).
/// <list type="bullet">
/// <item><see cref="SegmentId"/> != null: segment se PREPISUJE — isključuje se SAMO on iz postojećeg stanja, a ovaj zahtjev je
/// njegovo cijelo ciljno stanje (osobe = svi zaposlenici + svi klijenti koji će ga zauzimati, svi resursi);</item>
/// <item><see cref="SegmentId"/> == null: novi segment ili PRIRAST na postojećem (novo/reaktivirano sudjelovanje) — postojeći
/// segment ostaje u postojećem stanju, a <see cref="RoomPeople"/>/<see cref="Resources"/> su samo prirast.</item>
/// </list>
/// </summary>
public sealed record SegmentClaim(
    Guid? SegmentId,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd,
    IReadOnlyCollection<Guid> EmployeeIds,
    IReadOnlyCollection<Guid> ClientIds)
{
    /// <summary>Prostorija segmenta (null = bez prostorije).</summary>
    public Guid? RoomId { get; init; }

    /// <summary>Osobe u prostoriji koje ovaj zahtjev dodaje (vidi <see cref="RoomPeopleCount"/>).</summary>
    public int RoomPeople { get; init; }

    /// <summary>Količine resursa koje ovaj zahtjev dodaje.</summary>
    public IReadOnlyList<ResourceClaim> Resources { get; init; } = Array.Empty<ResourceClaim>();

    public bool Overlaps(SegmentClaim other) =>
        SchedulingInterval.Overlaps(PlannedStart, PlannedEnd, other.PlannedStart, other.PlannedEnd);

    public CapacityClaim CapacityOf(int amount) => new(PlannedStart, PlannedEnd, amount);

    /// <summary>Novi segment iz plana konstrukcijske jezgre (sudionici plana nastaju Confirmed — zauzimaju raspored i
    /// prostoriju; segment rezervira slot).</summary>
    public static SegmentClaim ForNew(SegmentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<Guid> employees = plan.EmployeeIds.Distinct().ToList();
        List<Guid> clients = plan.Participants.Select(p => p.ClientId).Distinct().ToList();
        return new SegmentClaim(null, plan.PlannedStart, plan.PlannedEnd, employees, clients)
        {
            RoomId = plan.RoomId,
            RoomPeople = RoomPeopleCount.Of(employees.Count, clients.Count),
            Resources = (plan.Resources ?? Array.Empty<SegmentResourcePlan>())
                .Select(r => new ResourceClaim(r.ResourceId, r.QuantityRequired)).ToList()
        };
    }

    /// <summary>
    /// Sudjelovanje klijenta koje POSTAJE zauzimajuće (Confirmed/Completed) na POSTOJEĆEM segmentu: novi Booking, gost,
    /// promocija s liste čekanja, novi član grupe ili reaktivacija (Cancelled/NoShow → Confirmed/Completed).
    /// Ako segment već rezervira slot, prirast je samo klijent (+1 osoba). Ako ga NE rezervira (termin eksplicitno otkazan,
    /// bez izvršenog ishoda), ova promjena ga ponovno aktivira: traže se i zaposlenici, svi resursi segmenta i SVE osobe
    /// (zaposlenici + zauzimajući klijenti + ovaj klijent). Termin mora biti učitan sa sudjelovanjima i dodjelama segmenta.
    /// </summary>
    public static SegmentClaim ForParticipationActivation(
        Appointment appointment, AppointmentSegment segment, Guid clientId, IReadOnlyList<ResourceClaim> segmentResources)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(segment);
        if (SegmentOccupancy.Reserves(appointment, segment))
            return new SegmentClaim(null, segment.PlannedStart, segment.PlannedEnd, Array.Empty<Guid>(), new[] { clientId })
            {
                RoomId = segment.RoomId,
                RoomPeople = 1
            };

        List<Guid> employees = segment.Employees.Select(e => e.EmployeeId).Distinct().ToList();
        int occupyingClients = appointment.Bookings
            .Where(b => b.ClientId != clientId)
            .SelectMany(b => b.Participations)
            .Count(p => p.AppointmentSegmentId == segment.Id && ParticipationOccupancy.Occupies(p.Status));
        return new SegmentClaim(null, segment.PlannedStart, segment.PlannedEnd, employees, new[] { clientId })
        {
            RoomId = segment.RoomId,
            RoomPeople = RoomPeopleCount.Of(employees.Count, occupyingClients + 1),
            Resources = segmentResources ?? Array.Empty<ResourceClaim>()
        };
    }
}

/// <summary>Količina resursa koju segment zauzima.</summary>
public sealed record ResourceClaim(Guid ResourceId, int Quantity);

/// <summary>
/// Phase M1D — broj OSOBA koje segment koji rezervira slot unosi u prostoriju: SVI dodijeljeni zaposlenici + sudjelovanja
/// koja zauzimaju raspored (Confirmed/Completed; Cancelled i NoShow nisu fizički prisutni). Segment koji NE rezervira slot
/// (SegmentOccupancy) unosi 0. Booking nije jedinica brojanja — broji se svako zauzimajuće sudjelovanje.
/// </summary>
public static class RoomPeopleCount
{
    public static int Of(int employeeCount, int occupyingParticipationCount) => employeeCount + occupyingParticipationCount;

    /// <summary>U memoriji, nad učitanim terminom (sudjelovanja) i segmentom (dodjele zaposlenika).</summary>
    public static int Of(Appointment appointment, AppointmentSegment segment) =>
        !SegmentOccupancy.Reserves(appointment, segment)
            ? 0
            : Of(segment.Employees.Count,
                appointment.Bookings.SelectMany(b => b.Participations)
                    .Count(p => p.AppointmentSegmentId == segment.Id && ParticipationOccupancy.Occupies(p.Status)));
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

    /// <summary>Phase M1D: kapacitet prostorija i resursa SAMO između segmenata ciljnog stanja (bez baze) — isti vremenski
    /// raslojen izračun kao i provjera nad bazom (vidi SchedulingConflictGuard). Kapacitet nepoznate prostorije/resursa se
    /// preskače (strukturna validacija je zaseban korak).</summary>
    public static void EnsureTargetStateCapacity(
        IReadOnlyList<SegmentClaim> claims, IReadOnlyDictionary<Guid, int> roomCapacities, IReadOnlyDictionary<Guid, int> resourceCapacities)
    {
        foreach (IGrouping<Guid, SegmentClaim> room in claims.Where(c => c.RoomId.HasValue && c.RoomPeople > 0).GroupBy(c => c.RoomId.Value))
        {
            if (!roomCapacities.TryGetValue(room.Key, out int capacity))
                continue;
            CapacityEvaluation evaluation = IntervalCapacity.Evaluate(
                Array.Empty<CapacityClaim>(), room.Select(c => c.CapacityOf(c.RoomPeople)).ToList(), capacity);
            if (evaluation.Exceeds)
                throw RoomCapacityExceeded(room.Key, null, capacity, evaluation);
        }

        foreach (IGrouping<Guid, (SegmentClaim Claim, ResourceClaim Resource)> resource in claims
                     .SelectMany(c => c.Resources.Select(r => (Claim: c, Resource: r)))
                     .GroupBy(x => x.Resource.ResourceId))
        {
            if (!resourceCapacities.TryGetValue(resource.Key, out int capacity))
                continue;
            CapacityEvaluation evaluation = IntervalCapacity.Evaluate(
                Array.Empty<CapacityClaim>(), resource.Select(x => x.Claim.CapacityOf(x.Resource.Quantity)).ToList(), capacity);
            if (evaluation.Exceeds)
                throw ResourceCapacityExceeded(resource.Key, null, capacity, evaluation);
        }
    }

    public static BusinessRuleException RoomCapacityExceeded(Guid roomId, string roomName, int capacity, CapacityEvaluation evaluation) =>
        new(ErrorCodes.RoomCapacityExceeded,
            $"Kapacitet prostorije{(roomName == null ? "" : $" '{roomName}'")} ({capacity} osoba) bio bi premašen (najviše istovremeno: {evaluation.PeakUsage}).",
            new { roomId, roomName, capacity, peakUsage = evaluation.PeakUsage, peakAt = evaluation.PeakAt });

    public static BusinessRuleException ResourceCapacityExceeded(Guid resourceId, string resourceName, int capacity, CapacityEvaluation evaluation) =>
        new(ErrorCodes.ResourceCapacityExceeded,
            $"Kapacitet resursa{(resourceName == null ? "" : $" '{resourceName}'")} ({capacity}) bio bi premašen (najviše istovremeno: {evaluation.PeakUsage}).",
            new { resourceId, resourceName, capacity, peakUsage = evaluation.PeakUsage, peakAt = evaluation.PeakAt });

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
/// <item>subjekti rasporeda — zaposlenici, klijenti, prostorije, resursi (Phase M1D), ključevi uzlazno; vrsta je u gornjem
/// bajtu ključa (0x41/0x42/0x43/0x44) pa je redoslijed vrsta posljedica istog sortiranja — UVIJEK prvi lock transakcije
/// koja ih treba (prostorija/resurs se zaključavaju kad upis može POVEĆATI njihovu zauzetost);</item>
/// <item>advisory lock slota grupe (generiranje occurrencea);</item>
/// <item>Appointment redak (FOR UPDATE);</item>
/// <item>sudjelovanja (FOR UPDATE, po Id-u);</item>
/// <item>ClientPackage redak.</item>
/// </list>
/// Iznimka: promocija s liste čekanja izvršava se UNUTAR transakcije koja već drži Appointment lock — zato subjekte (klijent,
/// prostorija) zaključava SAMO neblokirajuće (pg_try_advisory_xact_lock): nikad ne čeka, pa ne može zatvoriti ciklus; ako
/// lock nije odmah dostupan, promocija tog (i daljnjih, FIFO) čekatelja se odgađa za sljedeću priliku.
///
/// KLJUČEVI: 64-bitni advisory ključ = vrsta subjekta (gornji bajt, zaseban prostor po vrsti — isti Guid prostorije i
/// resursa nikad ne dijeli semantički identitet) + 56-bitni FNV-1a hash Guida. Sudar hasheva nepovezanih subjekata može
/// samo nepotrebno serijalizirati (lock je jedna vrijednost, redoslijed je totalan nad vrijednostima) — nikad dopustiti
/// kršenje invarijante.
/// </summary>
public static class SchedulingLockOrder
{
    private const byte EmployeeTag = 0x41;
    private const byte ClientTag = 0x42;
    private const byte RoomTag = 0x43;
    private const byte ResourceTag = 0x44;

    /// <summary>Uzlazno sortirani, deduplicirani ključevi: zaposlenici (0x41…), klijenti (0x42…), prostorije (0x43…), resursi
    /// (0x44…). Sudar dvaju ključeva
    /// iste vrste samo spaja lock (lock je jedna vrijednost) — redoslijed je totalni nad stvarnim vrijednostima locka.</summary>
    public static IReadOnlyList<long> Keys(
        IEnumerable<Guid> employeeIds, IEnumerable<Guid> clientIds, IEnumerable<Guid> roomIds = null, IEnumerable<Guid> resourceIds = null) =>
        (employeeIds ?? Enumerable.Empty<Guid>()).Select(EmployeeKey)
            .Concat((clientIds ?? Enumerable.Empty<Guid>()).Select(ClientKey))
            .Concat((roomIds ?? Enumerable.Empty<Guid>()).Select(RoomKey))
            .Concat((resourceIds ?? Enumerable.Empty<Guid>()).Select(ResourceKey))
            .Distinct()
            .OrderBy(k => k)
            .ToList();

    public static long EmployeeKey(Guid employeeId) => Key(EmployeeTag, employeeId);

    public static long ClientKey(Guid clientId) => Key(ClientTag, clientId);

    public static long RoomKey(Guid roomId) => Key(RoomTag, roomId);

    public static long ResourceKey(Guid resourceId) => Key(ResourceTag, resourceId);

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
