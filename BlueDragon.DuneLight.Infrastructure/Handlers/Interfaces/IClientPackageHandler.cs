using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IClientPackageHandler
{
    Task Add(ClientPackage clientPackage);
    Task<ClientPackage> GetById(Guid organizationId, Guid id);

    /// <summary>Kao <see cref="GetById(Guid, Guid)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task<ClientPackage> GetById(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Phase D3B3A: zaključava redak paketa (SELECT ... FOR UPDATE) u transakciji pa ga učitava sa
    /// ServiceEntries — svaka potrošnja/povrat istog paketa time se serijalizira (zadnji ulazak ne mogu potrošiti dvije
    /// transakcije). Null ako paket ne postoji u organizaciji.</summary>
    Task<ClientPackage> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id);

    /// <summary>Phase D3B3A: dodaje zapis potrošnje paketa (ledger) unutar transakcije.</summary>
    Task AddConsumption(IUnitOfWork uow, PackageConsumption consumption);

    /// <summary>Phase D3B3A: postoji li (u bazi, unutar transakcije) aktivna potrošnja sudjelovanja.</summary>
    Task<bool> HasActiveConsumption(IUnitOfWork uow, Guid participationId);

    /// <summary>Phase D3B3A: Consumed -&gt; Reversed na istom retku (nikad brisanje) — uvjetno na trenutno stanje u bazi;
    /// false ako je potrošnja već poništena (ponovljeno/konkurentno poništenje ne vraća ulazak dvaput).</summary>
    Task<bool> TryMarkReversed(IUnitOfWork uow, PackageConsumption consumption, Guid userId,
        Core.Enums.PackageConsumptionReversalReason reason, DateTimeOffset at);

    Task<List<ClientPackage>> GetByClient(Guid organizationId, Guid clientId);

    /// <summary>Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka (ili su neograničeni), valjani
    /// na datum izvođenja usluge — <paramref name="validityCutoff"/> je PackageValidity.ValidityCutoff tog datuma.</summary>
    Task<List<ClientPackage>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset validityCutoff);

    /// <summary>Sprema promjene na ClientPackage i njegovim ServiceEntries (koristi se za deduct/return ulaska).</summary>
    Task Update(ClientPackage clientPackage);

    /// <summary>Kao <see cref="Update(ClientPackage)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task Update(IUnitOfWork uow, ClientPackage clientPackage);

    Task<bool> HasAnyForClient(Guid organizationId, Guid clientId);

    /// <summary>Ima li klijent ijedan paket s efektivnim statusom Active (vidi ClientPackageStatusResolver —
    /// persistirani Status je Active I ExpiryDate još nije prošao). Expired/Depleted/Cancelled ne broje se.
    /// Koristi ClientService.Anonymize.</summary>
    Task<bool> HasUsableForClient(Guid organizationId, Guid clientId, DateTimeOffset now);
}
