using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientPackageHandler
{
    Task Add(ClientPackage clientPackage);
    Task<ClientPackage> GetById(Guid organizationId, Guid id);
    Task<List<ClientPackage>> GetByClient(Guid organizationId, Guid clientId);

    /// <summary>Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka (ili su neograničeni) na dani datum.</summary>
    Task<List<ClientPackage>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date);

    /// <summary>Sprema promjene na ClientPackage i njegovim ServiceEntries (koristi se za deduct/return ulaska).</summary>
    Task Update(ClientPackage clientPackage);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);
}
