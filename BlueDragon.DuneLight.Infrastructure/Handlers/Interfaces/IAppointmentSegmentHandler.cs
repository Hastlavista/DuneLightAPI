using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>
/// Phase D1 — minimalni persistence pristup segmentima termina (bez servisa i bez produkcijskih pozivatelja; zakazivanje
/// segmente još ne koristi). Svako čitanje je filtrirano po OrganizationId. Add sprema segment zajedno s njegovim
/// dodjelama zaposlenika/resursa; poslovna validacija (usluga/prostorija/zaposlenici/resursi pripadaju poslovnici
/// termina, kapaciteti) dolazi s prelaskom zakazivanja na segmente.
/// </summary>
public interface IAppointmentSegmentHandler
{
    Task Add(AppointmentSegment segment);

    /// <summary>Segment s uslugom, prostorijom, zaposlenicima i resursima.</summary>
    Task<AppointmentSegment> GetById(Guid organizationId, Guid id);

    /// <summary>Segmenti termina (s dodjelama), poredani po PlannedStart.</summary>
    Task<List<AppointmentSegment>> GetForAppointment(Guid organizationId, Guid appointmentId);
}
