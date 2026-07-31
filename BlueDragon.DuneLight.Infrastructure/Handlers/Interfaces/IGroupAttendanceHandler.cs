using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupAttendanceHandler
{
    /// <summary>Termin (Form=Group) s uključenim Group.Members(IsActive).Client i Attendances.Client — null ako ne
    /// postoji ili nije grupni termin.</summary>
    Task<Appointment> GetGroupAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Bare redak prisutnosti za pripremu mutacije, ili null ako ne postoji.</summary>
    Task<AppointmentAttendance> GetAttendanceRow(Guid organizationId, Guid appointmentId, Guid clientId);

    Task AddAttendance(AppointmentAttendance attendance);

    /// <summary>Kao <see cref="AddAttendance(AppointmentAttendance)"/>, ali unutar zajedničke transakcije
    /// (prisutnost + odbijanje/vraćanje ulaska iz paketa kao jedna atomična cjelina) — vidi IUnitOfWork.</summary>
    Task AddAttendance(IUnitOfWork uow, AppointmentAttendance attendance);

    Task UpdateAttendance(AppointmentAttendance attendance);

    /// <summary>Kao <see cref="UpdateAttendance(AppointmentAttendance)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task UpdateAttendance(IUnitOfWork uow, AppointmentAttendance attendance);
}
