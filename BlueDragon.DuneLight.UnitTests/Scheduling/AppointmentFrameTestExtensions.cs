#nullable disable
using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Test-only read convenience after the D3A storage cutover: the legacy Appointment frame properties no longer exist,
/// so characterization tests read the same values through the PRODUCTION single-segment frame
/// (<see cref="AppointmentFrame.Of"/> — the authoritative segment). Read-only and in-memory only: never usable in EF
/// queries, never available to production code. Requires the appointment to be loaded with Segments (+ Employees),
/// as SchedulingWorld.LoadAppointment does.
/// </summary>
public static class AppointmentFrameTestExtensions
{
    extension(Appointment appointment)
    {
        public DateTimeOffset StartsAt => AppointmentFrame.Of(appointment).StartsAt;
        public int DurationMinutes => AppointmentFrame.Of(appointment).DurationMinutes;
        public Guid ServiceId => AppointmentFrame.Of(appointment).ServiceId;
        public Guid? EmployeeId => AppointmentFrame.Of(appointment).EmployeeId;
        public Guid? RoomId => AppointmentFrame.Of(appointment).RoomId;
    }
}
