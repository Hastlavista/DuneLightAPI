using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>
/// Čitanje zauzetosti rasporeda (zaposlenik/prostorija/klijent) za provjere preklapanja, recurring/grupne batch provjere,
/// pauze i available-slots, te zaključavanje subjekata rasporeda. Vraća <see cref="OccupancySlot"/> (jedan po SEGMENTU).
///
/// Phase M1C:
/// <list type="bullet">
/// <item>preklapanje je poluotvoreno [start, end) i računa se u SQL-u istom formulom (SchedulingInterval) — bez ±1 dan
/// prozora;</item>
/// <item>zaposlenik/prostorija: samo segmenti koji rezerviraju slot (SegmentOccupancy — po segmentu, ne po
/// Appointment.Status); klijent: samo sudjelovanja koja zauzimaju raspored (ParticipationOccupancy);</item>
/// <item>isključenje je po SEGMENTU (<c>excludedSegmentIds</c>) — nikad cijeli termin, pa sestrinski segmenti istog termina
/// ostaju vidljivi;</item>
/// <item>pretraga zaposlenika i klijenta NIJE ograničena na poslovnicu (identitet subjekta je iznad poslovnice), uvijek
/// jest na organizaciju.</item>
/// </list>
/// Metode s <see cref="IUnitOfWork"/> čitaju unutar pozivateljeve transakcije (vide i njezine nespremljene upise) — za tvrde
/// provjere POD zaključanim subjektima (<see cref="LockSchedulingSubjects"/>).
/// </summary>
public interface ISchedulingOccupancyHandler
{
    /// <summary>Segmenti koji rezerviraju slot, s barem jednim od zaposlenika, preklapajući [start, end).</summary>
    Task<List<OccupancySlot>> GetOverlappingForEmployees(
        IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> employeeIds, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyCollection<Guid> excludedSegmentIds);

    /// <summary>Kao gore, izvan transakcije (vlastiti DbContext) — pauze zaposlenika.</summary>
    Task<List<OccupancySlot>> GetOverlappingForEmployee(
        Guid organizationId, Guid employeeId, DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Guid> excludedSegmentIds = null);

    /// <summary>Segmenti na kojima BAREM JEDAN od klijenata ima sudjelovanje koje zauzima raspored (Confirmed/Completed),
    /// preklapajući [start, end).</summary>
    Task<List<OccupancySlot>> GetOverlappingForClients(
        IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> clientIds, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyCollection<Guid> excludedSegmentIds);

    /// <summary>Phase M1D: zauzetost prostorije u OSOBAMA po segmentu koji rezervira slot i preklapa [start, end):
    /// svi dodijeljeni zaposlenici + zauzimajuća sudjelovanja (Confirmed/Completed). Isključuju se samo zadani segmenti.</summary>
    Task<List<CapacityClaim>> GetRoomUsage(
        IUnitOfWork uow, Guid organizationId, Guid roomId, DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Guid> excludedSegmentIds);

    /// <summary>Phase M1D: zauzetost resursa (QuantityRequired) po segmentu koji rezervira slot i preklapa [start, end).</summary>
    Task<List<CapacityClaim>> GetResourceUsage(
        IUnitOfWork uow, Guid organizationId, Guid resourceId, DateTimeOffset start, DateTimeOffset end, IReadOnlyCollection<Guid> excludedSegmentIds);

    /// <summary>Phase M1D: kapacitet i naziv prostorija organizacije (po Id-u).</summary>
    Task<Dictionary<Guid, (string Name, int Capacity)>> GetRoomCapacities(IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> roomIds);

    /// <summary>Phase M1D: kapacitet i naziv resursa organizacije (po Id-u).</summary>
    Task<Dictionary<Guid, (string Name, int Capacity)>> GetResourceCapacities(IUnitOfWork uow, Guid organizationId, IReadOnlyCollection<Guid> resourceIds);

    /// <summary>Phase M1D: resursi postojećeg segmenta (za prepisivanje vremena i reaktivaciju).</summary>
    Task<List<ResourceClaim>> GetSegmentResources(IUnitOfWork uow, Guid segmentId);

    /// <summary>Phase M1E: isto izvan transakcije (pročitano stanje za SegmentSnapshot prije zaključavanja).</summary>
    Task<List<ResourceClaim>> GetSegmentResources(Guid segmentId);

    /// <summary>Svi segmenti zaposlenika koji se preklapaju s [rangeFrom, rangeTo] — kandidati za batch provjere.</summary>
    Task<List<OccupancySlot>> GetForEmployeeInRange(Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali za više zaposlenika u jednom upitu (available-slots).</summary>
    Task<List<OccupancySlot>> GetForEmployeesInRange(Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali po prostoriji.</summary>
    Task<List<OccupancySlot>> GetForRoomInRange(Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Svi segmenti sa sudjelovanjem bilo kojeg od klijenata koje zauzima raspored, unutar raspona.</summary>
    Task<List<OccupancySlot>> GetForClientsInRange(Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Phase M1C: zaključava subjekte rasporeda (transakcijski advisory lock, redoslijed iz SchedulingLockOrder:
    /// zaposlenici pa klijenti, uzlazno). Mora biti PRVI lock u transakciji (prije Appointment/sudjelovanje/paket lockova).</summary>
    Task LockSchedulingSubjects(
        IUnitOfWork uow, IEnumerable<Guid> employeeIds, IEnumerable<Guid> clientIds, IEnumerable<Guid> roomIds = null, IEnumerable<Guid> resourceIds = null);

    /// <summary>Phase M1C: neblokirajući lock klijenta — za tokove koji već drže lock dalje u redoslijedu (promocija s liste
    /// čekanja pod Appointment lockom). false = lock trenutno drži druga transakcija.</summary>
    Task<bool> TryLockClientSchedule(IUnitOfWork uow, Guid clientId);

    /// <summary>Phase M1D: neblokirajući lock prostorije (promocija s liste čekanja).</summary>
    Task<bool> TryLockRoomSchedule(IUnitOfWork uow, Guid roomId);
}
