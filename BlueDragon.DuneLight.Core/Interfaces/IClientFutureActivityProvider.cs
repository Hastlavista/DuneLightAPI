using System;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Core.Interfaces;

/// <summary>
/// Mjesto pripremljeno za buduće module Termina i Prodanih paketa: provjerava je li klijent
/// ikad referenciran (budući ili povijesni termin, prodani paket), radi blokade tvrdog
/// brisanja. Dok ti moduli ne postoje, koristi se placeholder implementacija koja uvijek
/// vraća false.
/// </summary>
public interface IClientFutureActivityProvider
{
    Task<bool> HasFutureActivity(Guid organizationId, Guid clientId);
}
