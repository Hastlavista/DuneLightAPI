using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Clients;

namespace BlueDragon.DuneLight.Core.Interfaces.Clients;

public interface IClientPackageService
{
    Task<ClientPackageDto> Create(Guid organizationId, Guid userId, Guid clientId, ClientPackageCreateRequest request);
    Task<ClientPackageDto> GetById(Guid organizationId, Guid clientId, Guid id);
    Task<List<ClientPackageDto>> GetByClient(Guid organizationId, Guid clientId);

    /// <summary>Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka (ili su neograničeni), valjani na
    /// lokalni datum izvođenja usluge <paramref name="date"/> (kalendar poslovnice <paramref name="companyId"/>, inače
    /// organizacije). Potrošnja/povrat ulaska ide isključivo kroz ledger potrošnje (Phase D3B3A), ne kroz ovaj servis.</summary>
    Task<List<ClientPackageDto>> GetEligibleForService(
        Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date, Guid? companyId = null);

    /// <summary>Otkazuje paket — terminalno, sprječava buduće trošenje. Ne briše i ne vraća ulaske.</summary>
    Task<ClientPackageDto> Cancel(Guid organizationId, Guid clientId, Guid id, Guid userId);
}
