using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IServiceCompanyHandler
{
    /// <summary>Sve dodjele za uslugu, tenant-provjereno preko Service.OrganizationId. Company navigacija je učitana.</summary>
    Task<List<ServiceCompany>> GetForService(Guid organizationId, Guid serviceId);

    /// <summary>Sve dodjele za poslovnicu, tenant-provjereno preko Company.OrganizationId. Service navigacija je učitana.</summary>
    Task<List<ServiceCompany>> GetForCompany(Guid organizationId, Guid companyId);

    /// <summary>Service.IsActive AND Company.IsActive AND postoji dodjela — buduće domene (Appointment/Group) ovo
    /// koriste za provjeru "je li usluga stvarno bookabilna u ovoj poslovnici".</summary>
    Task<bool> IsAvailable(Guid organizationId, Guid serviceId, Guid companyId);

    /// <summary>
    /// Atomarno postavlja konačan skup CompanyId za uslugu u jednom SaveChanges pozivu (briše izostavljene,
    /// dodaje nove) — sve companyIds su već tenant/aktivnost-provjerene kod pozivatelja
    /// (ServiceAvailabilityService.ReplaceAssignedCompanies), pa handler ne radi dodatnu validaciju.
    /// </summary>
    Task ReplaceForService(Guid serviceId, List<Guid> companyIds);
}
