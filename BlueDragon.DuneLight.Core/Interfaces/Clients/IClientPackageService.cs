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

    /// <summary>Aktivni paketi klijenta koji pokrivaju uslugu i imaju preostalih ulazaka (ili su neograničeni) na dani datum.</summary>
    Task<List<ClientPackageDto>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date);

    /// <summary>Skida jedan ulazak (SharedPool ili PerService, ovisno o EntryMode). Koristi AppointmentService kod naplate.</summary>
    Task DeductEntry(Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId);

    /// <summary>Vraća jedan ulazak — eksplicitna akcija, nikad automatska.</summary>
    Task ReturnEntry(Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId);
}
