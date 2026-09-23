using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Groups;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IWaitlistHandler
{
    Task Add(IUnitOfWork uow, WaitlistEntry entry);
    Task Update(IUnitOfWork uow, WaitlistEntry entry);

    /// <summary>Puna lista (svi statusi, uklj. povijest) po Appointmentu, s Client uključenim — za GET roster
    /// prikaz. Redoslijed se dovršava u servisu (Waiting prvo po JoinedAt/Id, ostalo poslije).</summary>
    Task<List<WaitlistEntry>> GetForAppointment(Guid organizationId, Guid appointmentId);

    /// <summary>Trenutno AKTIVAN (Waiting) redak za (appointment, client), ili null — koristi se za
    /// eligibility provjeru kod Join (ALREADY_WAITLISTED).</summary>
    Task<WaitlistEntry> GetActiveForClient(Guid organizationId, Guid appointmentId, Guid clientId);

    /// <summary>Najnoviji redak (bilo kojeg statusa) za (appointment, client) — koristi se za idempotentan Cancel.</summary>
    Task<WaitlistEntry> GetMostRecentForClient(Guid organizationId, Guid appointmentId, Guid clientId);

    /// <summary>Ima li klijent ijedan TRENUTNO aktivan (Waiting) redak, na bilo kojem terminu — koristi
    /// ClientService.Anonymize (vidi spec section 57).</summary>
    Task<bool> HasActiveWaitingForClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent BILO KOJI waitlist redak (bilo kojeg statusa — Waiting/Promoted/Cancelled/Expired,
    /// povijesni uklj.) — waitlist_entries.client_id FK nema ON DELETE pravilo (isti obrazac kao
    /// group_members.client_id), pa bez ove provjere ClientService.Delete može propasti sirovom Postgres FK
    /// greškom umjesto čistom REFERENCED_CANNOT_DELETE porukom (vidi ClientFutureActivityProvider).</summary>
    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);

    /// <summary>Svi TRENUTNO aktivni (Waiting) redci preko zadanih Appointment ID-eva u jednom upitu — za
    /// OperationalDashboardService (grupira se u memoriji po AppointmentId, izbjegava upit po terminu u petlji).</summary>
    Task<List<WaitlistEntry>> GetWaitingForAppointments(Guid organizationId, List<Guid> appointmentIds);
}
