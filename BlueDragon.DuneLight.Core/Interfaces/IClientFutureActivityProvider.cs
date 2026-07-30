using System;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Core.Interfaces;

/// <summary>
/// Provjerava je li klijent ikad referenciran (budući ili povijesni termin, prodani paket),
/// radi blokade tvrdog brisanja. Implementacija (ClientFutureActivityProvider) upitava module
/// Termina i Prodanih paketa; koristi je ClientService.Delete.
/// </summary>
public interface IClientFutureActivityProvider
{
    Task<bool> HasFutureActivity(Guid organizationId, Guid clientId);
}
