using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGroupAttendanceHandler
{
    /// <summary>Termin (Form=Group) s uključenim Group.Members(IsActive).Client i Attendances.Client — null ako ne
    /// postoji ili nije grupni termin.</summary>
    Task<Appointment> GetGroupAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Bare redak prisutnosti za pripremu mutacije, ili null ako ne postoji.</summary>
    Task<AppointmentAttendance> GetAttendanceRow(Guid appointmentId, Guid clientId);

    Task AddAttendance(AppointmentAttendance attendance);
    Task UpdateAttendance(AppointmentAttendance attendance);
}
