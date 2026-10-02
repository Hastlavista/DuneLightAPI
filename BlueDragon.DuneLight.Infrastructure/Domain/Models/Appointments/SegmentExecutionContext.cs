using System;
using System.Collections.Generic;

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
        Guid organizationId, Guid appointmentId, Guid segmentId, Guid companyId, Guid serviceId, string serviceName,
        IReadOnlyList<Guid> employeeIds, Guid? pricingEmployeeId, DateTimeOffset startsAt)
    {
        OrganizationId = organizationId;
        AppointmentId = appointmentId;
        SegmentId = segmentId;
        CompanyId = companyId;
        ServiceId = serviceId;
        ServiceName = serviceName;
        EmployeeIds = employeeIds;
        PricingEmployeeId = pricingEmployeeId;
        StartsAt = startsAt;
    }

    public Guid OrganizationId { get; }

    public Guid AppointmentId { get; }

    public Guid SegmentId { get; }

    public Guid CompanyId { get; }

    public Guid ServiceId { get; }

    /// <summary>Naziv usluge segmenta — null ako navigacija nije učitana (jedini potrošač je opis CheckoutItem stavke).</summary>
    public string ServiceName { get; }

    /// <summary>Phase M1G — SVI zaposlenici segmenta (ravnopravni izvršitelji, bez "glavnog"; prazno bez zaposlenika).
    /// Provizija se računa neovisno za svakog.</summary>
    public IReadOnlyList<Guid> EmployeeIds { get; }

    /// <summary>Phase M1G — zaposlenik čije razine cjenika koristi izvor cijene segmenta (Employee); null = Standard.
    /// Samo za cijenu — NIJE korisnik provizije ni vlasnik.</summary>
    public Guid? PricingEmployeeId { get; }

    /// <summary>Planirani početak segmenta — datum za cijenu, valjanost paketa i rok otkazivanja.</summary>
    public DateTimeOffset StartsAt { get; }
}
