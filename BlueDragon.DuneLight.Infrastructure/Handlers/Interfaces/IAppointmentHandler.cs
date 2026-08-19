using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Appointments;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAppointmentHandler
{
    /// <summary>appointment.Clients mora biti popunjen prije poziva — cascade insert.</summary>
    Task Add(Appointment appointment);

    /// <summary>Kao <see cref="Add(Appointment)"/>, ali unutar zajedničke transakcije (npr. termin + odbijanje ulaska
    /// iz paketa + audit log kao jedna atomična cjelina) — vidi IUnitOfWork.</summary>
    Task Add(IUnitOfWork uow, Appointment appointment);

    Task<Appointment> GetById(Guid organizationId, Guid id);

    /// <summary>Bare redak, BEZ Clients/Service/Employee/Company navigacija — za pripremu mutacije (izbjegava EF tracking sudar).</summary>
    Task<Appointment> GetByIdLight(Guid organizationId, Guid id);

    /// <summary>Batch verzija za listu klijenata u jednom upitu (izbjegava N+1) — unutar zajedničke transakcije.</summary>
    Task<List<AppointmentClient>> GetAppointmentClients(IUnitOfWork uow, Guid organizationId, Guid appointmentId, List<Guid> clientIds);

    /// <summary>Samo skalarna polja termina, bez diranja Clients retka — koristi se za Complete/Cancel/NoShow prijelaze.</summary>
    Task UpdateScalar(Appointment appointment);

    /// <summary>Kao <see cref="UpdateScalar(Appointment)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateScalar(IUnitOfWork uow, Appointment appointment);

    /// <summary>Puna izmjena uklj. popis klijenata — spaja postojeće AppointmentClient retke (čuva plaćanje/paket), uklanja izbačene, dodaje nove.</summary>
    Task UpdateWithClients(Appointment appointment, List<Guid> clientIds);

    /// <summary>Kao <see cref="UpdateWithClients(Appointment, List{Guid})"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateWithClients(IUnitOfWork uow, Appointment appointment, List<Guid> clientIds);

    Task UpdateAppointmentClient(AppointmentClient appointmentClient);

    /// <summary>Kao <see cref="UpdateAppointmentClient(AppointmentClient)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateAppointmentClient(IUnitOfWork uow, AppointmentClient appointmentClient);

    Task Delete(Appointment appointment);

    Task<List<Appointment>> GetOverlappingForEmployee(Guid organizationId, Guid employeeId, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    Task<List<Appointment>> GetOverlappingForClients(Guid organizationId, List<Guid> clientIds, DateTimeOffset startsAt, int durationMinutes, Guid? excludeId);

    /// <summary>Svi termini trenera unutar raspona (bez otkazanih/izostalih) — kandidati za preklapanje cijelog
    /// recurring niza odjednom, precizna provjera po occurrenceu radi se u servisu u memoriji.</summary>
    Task<List<Appointment>> GetForEmployeeInRange(Guid organizationId, Guid employeeId, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    /// <summary>Svi termini bilo kojeg od klijenata unutar raspona (bez otkazanih/izostalih) — kandidati za
    /// preklapanje cijelog recurring niza odjednom, precizna provjera po occurrenceu radi se u servisu u memoriji.</summary>
    Task<List<Appointment>> GetForClientsInRange(Guid organizationId, List<Guid> clientIds, DateTimeOffset rangeFrom, DateTimeOffset rangeTo);

    Task<List<Appointment>> GetForSchedule(Guid organizationId, AppointmentScheduleQuery query);

    /// <summary>Batch insert za recurring niz — appointment.Clients mora biti popunjen za svaki termin prije poziva, jedan SaveChangesAsync za cijeli niz.</summary>
    Task AddRange(List<Appointment> appointments);

    Task<(List<Appointment> Items, int TotalCount)> GetByClient(Guid organizationId, Guid clientId, PagedRequest request);

    Task<bool> HasFutureScheduledForEmployee(Guid organizationId, Guid employeeId);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);
}
