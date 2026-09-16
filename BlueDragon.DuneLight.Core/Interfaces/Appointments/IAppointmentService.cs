using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Appointments;

public interface IAppointmentService
{
    /// <summary>"Zakaži" — status Scheduled, bez naplate.</summary>
    Task<AppointmentDto> Create(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCreateRequest request);

    /// <summary>"Upiši odrađeno" — novi termin odmah u statusu Completed, naplata odmah.</summary>
    Task<AppointmentDto> CompleteNew(Guid organizationId, Guid userId, bool hasFullScope, AppointmentCompleteRequest request);

    /// <summary>Prijelaz postojećeg (obično Scheduled) termina u Completed, naplata odmah. Samo Form=Individual
    /// — za Form=Group koristi CompleteGroupAppointment.</summary>
    Task<AppointmentDto> CompleteExisting(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCompleteRequest request);

    /// <summary>Appointment-razina "odrađeno" za GRUPNI termin — samo prijelaz Scheduled → Completed, ne dira
    /// Booking retke (svaki se razrješava neovisno kroz BookingService.SetStatus/GroupAttendanceService).
    /// Upozorava (ne blokira) ako neki Booking ostane Confirmed u trenutku zatvaranja.</summary>
    Task<AppointmentDto> CompleteGroupAppointment(Guid organizationId, Guid userId, bool hasFullScope, Guid id);

    /// <summary>Izmjena vremena/usluge/trenera/tvrtke/klijenata/napomene/iznosa. Ne dira plaćanje/paket.</summary>
    Task<AppointmentDto> Update(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentUpdateRequest request);

    /// <summary>Brzo pomicanje (drag-and-drop) — samo StartsAt/trener/tvrtka, bez diranja ostalih polja.</summary>
    Task<AppointmentDto> Move(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentMoveRequest request);

    Task<AppointmentDto> Cancel(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request);

    Task<AppointmentDto> MarkNoShow(Guid organizationId, Guid userId, bool hasFullScope, Guid id, AppointmentCancelRequest request);

    /// <summary>Tvrdo brisanje — samo Admin, samo isti dan kad je unesen (provjerava se u servisu).</summary>
    Task Delete(Guid organizationId, Guid userId, Guid id);

    Task<List<AppointmentDto>> CreateRecurring(Guid organizationId, Guid userId, bool hasFullScope, RecurringAppointmentCreateRequest request);

    Task<List<AppointmentScheduleCellDto>> GetSchedule(Guid organizationId, AppointmentScheduleQuery query);

    Task<AppointmentDto> GetById(Guid organizationId, Guid id);

    Task<PagedResult<ClientAppointmentHistoryDto>> GetByClient(Guid organizationId, Guid clientId, PagedRequest request);

    /// <summary>Povijest odrađenih termina po zaposleniku (samo Completed), najnoviji prvi — vidi GetByClient.</summary>
    Task<PagedResult<AppointmentDto>> GetByEmployee(Guid organizationId, Guid employeeId, PagedRequest request);

    /// <summary>Gotovi, izrezani slobodni termini točne duljine usluge, za sve zaposlenike poslovnice koji smiju
    /// tu uslugu izvoditi (ili samo za query.EmployeeId ako je zadan) — za "Pronađi dostupan termin" u formi novog termina.</summary>
    Task<List<EmployeeAvailableSlotsDto>> GetAvailableSlots(Guid organizationId, AvailableSlotsQuery query);
}
