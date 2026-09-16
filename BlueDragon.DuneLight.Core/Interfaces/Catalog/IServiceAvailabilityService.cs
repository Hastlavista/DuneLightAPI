using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;

namespace BlueDragon.DuneLight.Core.Interfaces.Catalog;

/// <summary>
/// Upravlja eksplicitnom dostupnošću Service ↔ Company (ServiceCompany junction). Vidi ServiceCompany
/// domensku napomenu: prazan popis dodjela znači "usluga dostupna nigdje", nikad implicitno "svugdje".
/// </summary>
public interface IServiceAvailabilityService
{
    Task<List<CompanyDto>> GetAssignedCompanies(Guid organizationId, Guid serviceId);
    Task<List<ServiceDto>> GetAssignedServices(Guid organizationId, Guid companyId);
    Task<List<CompanyDto>> ReplaceAssignedCompanies(Guid organizationId, Guid userId, Guid serviceId, ReplaceServiceCompaniesRequest request);

    /// <summary>Service.IsActive AND Company.IsActive AND postoji ServiceCompany dodjela. Namijenjeno budućim
    /// modulima (Appointment/Group) za provjeru je li usluga stvarno bookabilna u poslovnici — ne koristi se
    /// još nigdje u ovom zahvatu.</summary>
    Task<bool> IsServiceAvailableAtCompany(Guid organizationId, Guid serviceId, Guid companyId);
}
