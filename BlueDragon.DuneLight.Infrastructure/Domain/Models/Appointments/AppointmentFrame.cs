using System;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Današnji jednostruki izvršni okvir termina — usluga, trener, prostorija i vrijeme — kako ga pišu kreiranje
/// (<see cref="Utils.AppointmentFactory"/>) i izmjena (<see cref="Utils.AppointmentFrameMutator"/>). U ciljnom modelu ovo
/// postaje AppointmentSegment; poslovnica (CompanyId) NIJE dio okvira jer ostaje na razini termina.
/// Nije EF entitet i nema tablicu.
/// </summary>
public sealed record AppointmentFrame(
    Guid ServiceId,
    Guid? EmployeeId,
    Guid? RoomId,
    DateTimeOffset StartsAt,
    int DurationMinutes)
{
    /// <summary>Trenutni okvir učitanog (ili upravo izmijenjenog) termina — bez ponovnog čitanja iz baze.</summary>
    public static AppointmentFrame Of(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        return new AppointmentFrame(
            appointment.ServiceId, appointment.EmployeeId, appointment.RoomId, appointment.StartsAt, appointment.DurationMinutes);
    }
}
