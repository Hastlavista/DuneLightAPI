using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Provjerava je li klijent ikad referenciran (bilo koji termin, bilo koji prodani paket) — blokira tvrdo brisanje.</summary>
public class ClientFutureActivityProvider : IClientFutureActivityProvider
{
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientPackageHandler _clientPackageHandler;

    public ClientFutureActivityProvider(IAppointmentHandler appointmentHandler, IClientPackageHandler clientPackageHandler)
    {
        _appointmentHandler = appointmentHandler;
        _clientPackageHandler = clientPackageHandler;
    }

    public async Task<bool> HasFutureActivity(Guid organizationId, Guid clientId)
    {
        if (await _appointmentHandler.HasAnyForClient(organizationId, clientId))
            return true;

        return await _clientPackageHandler.HasAnyForClient(organizationId, clientId);
    }
}
