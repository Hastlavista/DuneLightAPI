using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Provjerava je li klijent ikad referenciran (bilo koji termin, bilo koji prodani paket, bilo koje
/// članstvo u grupi, bilo koji waitlist redak — aktivno ili povijesno) — blokira tvrdo brisanje.
/// group_members.client_id i waitlist_entries.client_id FK-ovi nemaju ON DELETE pravilo, pa bez ove provjere
/// ClientService.Delete bi propao na sirovoj Postgres FK grešci umjesto na REFERENCED_CANNOT_DELETE (Data/
/// Lifecycle Consistency Cleanup — waitlist je bio izostavljen ovdje unatoč istom FK obrascu kao group_members).</summary>
public class ClientFutureActivityProvider : IClientFutureActivityProvider
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IGroupHandler _groupHandler;
    private readonly IWaitlistHandler _waitlistHandler;

    public ClientFutureActivityProvider(
        IAppointmentHandler appointmentHandler, IClientPackageHandler clientPackageHandler, IGroupHandler groupHandler, IWaitlistHandler waitlistHandler)
    {
        _appointmentHandler = appointmentHandler;
        _clientPackageHandler = clientPackageHandler;
        _groupHandler = groupHandler;
        _waitlistHandler = waitlistHandler;
    }

    public async Task<bool> HasFutureActivity(Guid organizationId, Guid clientId)
    {
        if (await _appointmentHandler.HasAnyForClient(organizationId, clientId))
            return true;

        if (await _clientPackageHandler.HasAnyForClient(organizationId, clientId))
            return true;

        if (await _groupHandler.HasAnyMembershipForClient(organizationId, clientId))
            return true;

        return await _waitlistHandler.HasAnyForClient(organizationId, clientId);
    }
}
