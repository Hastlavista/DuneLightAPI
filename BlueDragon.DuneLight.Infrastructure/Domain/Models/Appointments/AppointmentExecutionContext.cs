using System;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Izvršni kontekst jednog termina kako ga vide cijena, paketi, provizija i blagajna: koja usluga se izvodi, u kojoj
/// poslovnici, tko je izvodi i kada. Danas je to 1:1 preslika jednog Appointment okvira (ServiceId/EmployeeId/StartsAt);
/// u ciljnom modelu to postaje AppointmentSegment. Nije EF entitet i nema tablicu — stvara se isključivo kroz
/// <see cref="Utils.ExecutionContextResolver"/>.
///
/// Namjerno uzak: sadrži samo ono što postojeći komercijalni pozivatelji stvarno čitaju (RoomId/trajanje nemaju
/// komercijalnog potrošača — zauzetost rasporeda ide kroz OccupancySlot).
/// </summary>
public record AppointmentExecutionContext
{
    public AppointmentExecutionContext(
        Guid organizationId, Guid appointmentId, Guid companyId, Guid serviceId, string serviceName, Guid? employeeId, DateTimeOffset startsAt)
    {
        OrganizationId = organizationId;
        AppointmentId = appointmentId;
        CompanyId = companyId;
        ServiceId = serviceId;
        ServiceName = serviceName;
        EmployeeId = employeeId;
        StartsAt = startsAt;
    }

    public Guid OrganizationId { get; }

    public Guid AppointmentId { get; }

    public Guid CompanyId { get; }

    public Guid ServiceId { get; }

    /// <summary>Naziv usluge iz Appointment.Service navigacije — null ako navigacija nije učitana (jedini potrošač je
    /// opis CheckoutItem stavke, koji učitava Service i sam ima "Booking" fallback).</summary>
    public string ServiceName { get; }

    /// <summary>Null za grupni termin bez dodijeljenog trenera.</summary>
    public Guid? EmployeeId { get; }

    /// <summary>Planirani početak izvođenja — datum za cijenu, valjanost paketa i rok otkazivanja.</summary>
    public DateTimeOffset StartsAt { get; }
}
