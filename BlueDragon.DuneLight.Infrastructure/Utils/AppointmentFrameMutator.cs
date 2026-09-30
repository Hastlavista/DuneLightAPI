using System;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// JEDINO mjesto koje piše jednostruka polja izvršnog okvira termina (ServiceId/EmployeeId/RoomId/StartsAt/
/// DurationMinutes) — kreiranje (<see cref="AppointmentFactory"/>) i izmjena (CompleteExisting/Update/Move).
/// Mijenja samo učitani entitet u memoriji: bez čitanja iz baze, bez spremanja, bez validacije, bez audita — sve to
/// ostaje kod pozivatelja, isto kao prije. Djelomične izmjene (Move: null = "bez promjene") pozivatelj izražava kroz
/// <c>AppointmentFrame.Of(appointment) with { ... }</c>, pa nepromijenjena polja zadržavaju točno postojeće vrijednosti.
/// </summary>
public static class AppointmentFrameMutator
{
    public static void Apply(Appointment appointment, AppointmentFrame frame)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(frame);

        appointment.ServiceId = frame.ServiceId;
        appointment.EmployeeId = frame.EmployeeId;
        appointment.RoomId = frame.RoomId;
        appointment.StartsAt = frame.StartsAt;
        appointment.DurationMinutes = frame.DurationMinutes;
    }
}
