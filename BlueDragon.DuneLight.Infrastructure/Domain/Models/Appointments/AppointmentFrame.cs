using System;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Današnji jednostruki izvršni okvir termina — usluga, trener, prostorija i vrijeme. Od Phase D3A ga autoritativno nosi
/// JEDINI segment termina (<see cref="AppointmentSegment"/>): kreiranje (<see cref="Utils.AppointmentFactory"/>) ga upisuje
/// u novi segment, izmjena (<see cref="Utils.AppointmentFrameMutator"/>) u postojeći. Poslovnica (CompanyId) NIJE dio
/// okvira jer ostaje na razini termina. Nije EF entitet i nema tablicu.
/// </summary>
public sealed record AppointmentFrame(
    Guid ServiceId,
    Guid? EmployeeId,
    Guid? RoomId,
    DateTimeOffset StartsAt,
    int DurationMinutes)
{
    public DateTimeOffset EndsAt => StartsAt.AddMinutes(DurationMinutes);

    /// <summary>Trenutni okvir učitanog (ili upravo izmijenjenog) termina iz njegovog jedinog segmenta — bez ponovnog
    /// čitanja iz baze. Zahtijeva učitane Segments i Employees.</summary>
    public static AppointmentFrame Of(Appointment appointment)
    {
        AppointmentSegment segment = AppointmentSegments.GetSingleExecutionSegment(appointment);
        return new AppointmentFrame(
            segment.ServiceId,
            AppointmentSegments.GetSingleEmployeeId(segment),
            segment.RoomId,
            segment.PlannedStart,
            AppointmentSegments.DurationMinutes(segment));
    }
}
