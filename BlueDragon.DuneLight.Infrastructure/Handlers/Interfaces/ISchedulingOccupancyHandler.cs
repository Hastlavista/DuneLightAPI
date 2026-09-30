using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>
/// Čitanje zauzetosti rasporeda (termini trenera/prostorije/klijenta) za provjere preklapanja, recurring/grupne
/// batch provjere, pauze i available-slots. Vraća <see cref="OccupancySlot"/> umjesto Appointment entiteta, tako da
/// pozivatelji ne ovise o jednom ServiceId/EmployeeId/RoomId/StartsAt obliku termina.
///
/// Svi upiti isključuju termine sa Status = Cancelled. Metode "Overlapping" dohvaćaju kandidate čiji StartsAt pada
/// unutar ±1 dan od traženog početka i zatim u memoriji zadržavaju samo stvarna preklapanja (susjedni intervali nisu
/// sudar); metode "InRange" vraćaju sve termine čiji StartsAt pada unutar [rangeFrom, rangeTo] (uključivo), a precizna
/// provjera po occurrenceu radi se kod pozivatelja.
/// </summary>
public interface ISchedulingOccupancyHandler
{
    Task<List<OccupancySlot>> GetOverlappingForEmployee(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Termini gdje BAREM JEDAN od zadanih klijenata ima AKTIVAN Booking (Confirmed/Completed — ne
    /// Cancelled/NoShow) koji se preklapa s traženim intervalom.</summary>
    Task<List<OccupancySlot>> GetOverlappingForClients(Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    Task<List<OccupancySlot>> GetOverlappingForRoom(Guid organizationId, Guid roomId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Svi termini trenera unutar raspona — kandidati za preklapanje cijelog recurring niza odjednom.</summary>
    Task<List<OccupancySlot>> GetForEmployeeInRange(Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali za više zaposlenika u jednom upitu (available-slots).</summary>
    Task<List<OccupancySlot>> GetForEmployeesInRange(Guid organizationId, List<Guid> employeeIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Kao <see cref="GetForEmployeeInRange"/>, ali po prostoriji.</summary>
    Task<List<OccupancySlot>> GetForRoomInRange(Guid organizationId, Guid roomId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Svi termini s AKTIVNIM Bookingom bilo kojeg od klijenata unutar raspona.</summary>
    Task<List<OccupancySlot>> GetForClientsInRange(Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);
}
