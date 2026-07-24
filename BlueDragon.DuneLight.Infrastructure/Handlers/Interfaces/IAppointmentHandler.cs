using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAppointmentHandler
{
    /// <summary>appointment.Clients mora biti popunjen prije poziva — cascade insert.</summary>
    Task Add(Appointment appointment);

    Task<Appointment> GetById(Guid organizationId, Guid id);

    /// <summary>Bare redak, BEZ Clients/Service/Employee/Location navigacija — za pripremu mutacije (izbjegava EF tracking sudar).</summary>
    Task<Appointment> GetByIdLight(Guid organizationId, Guid id);

    /// <summary>Jedan AppointmentClient redak, bare — za ciljanu mutaciju plaćanja/paketa jednog klijenta.</summary>
    Task<AppointmentClient> GetAppointmentClient(Guid organizationId, Guid appointmentId, Guid clientId);

    /// <summary>Samo skalarna polja termina, bez diranja Clients retka — koristi se za Complete/Cancel/NoShow prijelaze.</summary>
    Task UpdateScalar(Appointment appointment);

    /// <summary>Puna izmjena uklj. popis klijenata — spaja postojeće AppointmentClient retke (čuva plaćanje/paket), uklanja izbačene, dodaje nove.</summary>
    Task UpdateWithClients(Appointment appointment, List<Guid> clientIds);

    Task UpdateAppointmentClient(AppointmentClient appointmentClient);

    Task Delete(Appointment appointment);

    Task<List<Appointment>> GetOverlappingForEmployee(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    Task<List<Appointment>> GetOverlappingForClients(Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query);

    Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request);

    Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);
}
