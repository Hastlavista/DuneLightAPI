using System;

namespace BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

/// <summary>
/// Phase M1B — izvršni kontekst JEDNOG SEGMENTA (konkretne izvršne jedinice) kako ga vide cijena, paketi, provizija i
/// blagajna: koja usluga se izvodi, u kojoj poslovnici, tko je izvodi i kada. Termin NEMA izvršni kontekst (može imati
/// više usluga/zaposlenika/početaka) — svaka komercijalna/izvršna operacija adresira segment ili sudjelovanje. Nije EF
/// entitet i nema tablicu — stvara se isključivo kroz <see cref="Utils.ExecutionContextResolver"/>.
///
/// Namjerno uzak: sadrži samo ono što postojeći komercijalni pozivatelji stvarno čitaju (RoomId/trajanje nemaju
/// komercijalnog potrošača — zauzetost rasporeda ide kroz OccupancySlot).
/// </summary>
public record SegmentExecutionContext
{
    public SegmentExecutionContext(
        Guid organizationId, Guid appointmentId, Guid segmentId, Guid companyId, Guid serviceId, string serviceName, Guid? employeeId,
        DateTimeOffset startsAt)
    {
        OrganizationId = organizationId;
        AppointmentId = appointmentId;
        SegmentId = segmentId;
        CompanyId = companyId;
        ServiceId = serviceId;
        ServiceName = serviceName;
        EmployeeId = employeeId;
        StartsAt = startsAt;
    }

    public Guid OrganizationId { get; }

    public Guid AppointmentId { get; }

    public Guid SegmentId { get; }

    public Guid CompanyId { get; }

    public Guid ServiceId { get; }

    /// <summary>Naziv usluge segmenta — null ako navigacija nije učitana (jedini potrošač je opis CheckoutItem stavke).</summary>
    public string ServiceName { get; }

    /// <summary>Zaposlenik segmenta (danas najviše jedan — ograničenje proizvoda, ne sheme); null bez zaposlenika.</summary>
    public Guid? EmployeeId { get; }

    /// <summary>Planirani početak segmenta — datum za cijenu, valjanost paketa i rok otkazivanja.</summary>
    public DateTimeOffset StartsAt { get; }
}
