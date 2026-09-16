using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Provjerava je li klijent ikad referenciran (bilo koji termin, bilo koji prodani paket, bilo koje
/// članstvo u grupi — aktivno ili povijesno) — blokira tvrdo brisanje. group_members.client_id FK nema
/// ON DELETE pravilo, pa bez ove provjere ClientService.Delete bi propao na sirovoj Postgres FK grešci
/// umjesto na REFERENCED_CANNOT_DELETE.</summary>
public class ClientFutureActivityProvider : IClientFutureActivityProvider
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IGroupHandler _groupHandler;

    public ClientFutureActivityProvider(
        IAppointmentHandler appointmentHandler, IClientPackageHandler clientPackageHandler, IGroupHandler groupHandler)
    {
        _appointmentHandler = appointmentHandler;
        _clientPackageHandler = clientPackageHandler;
        _groupHandler = groupHandler;
    }

    public async Task<bool> HasFutureActivity(Guid organizationId, Guid clientId)
    {
        if (await _appointmentHandler.HasAnyForClient(organizationId, clientId))
            return true;

        if (await _clientPackageHandler.HasAnyForClient(organizationId, clientId))
            return true;

        return await _groupHandler.HasAnyMembershipForClient(organizationId, clientId);
    }
}
