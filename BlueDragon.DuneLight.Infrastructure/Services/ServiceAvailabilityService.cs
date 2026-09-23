using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Upravlja ServiceCompany dodjelama — eksplicitnom dostupnošću Service (Organization-level kataloška
/// stavka) po Company (poslovnica). Namjerno NEMA "prazno = svugdje": nova usluga i nova poslovnica
/// kreću bez ijedne dodjele (vidi ServiceCompany). Postojeće dodjele PREŽIVE deaktivaciju bilo koje
/// strane (grandfathering) — samo NOVE dodjele zahtijevaju da su Service i ciljna Company aktivni.
/// </summary>
public class ServiceAvailabilityService : IServiceAvailabilityService
{
    private readonly IServiceHandler _serviceHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IServiceCompanyHandler _serviceCompanyHandler;

    public ServiceAvailabilityService(
        IServiceHandler serviceHandler,
        ICompanyHandler companyHandler,
        IServiceCompanyHandler serviceCompanyHandler)
    {
        _serviceHandler = serviceHandler;
        _companyHandler = companyHandler;
        _serviceCompanyHandler = serviceCompanyHandler;
    }

    public async Task<List<CompanyDto>> GetAssignedCompanies(Guid organizationId, Guid serviceId)
    {
        await EnsureServiceExists(organizationId, serviceId);

        List<ServiceCompany> assignments = await _serviceCompanyHandler.GetForService(organizationId, serviceId);
        return assignments
            .OrderBy(sc => sc.Company.SortOrder)
            .ThenBy(sc => sc.Company.Name)
            .Select(sc => ToCompanyDto(sc.Company))
            .ToList();
    }

    public async Task<List<ServiceDto>> GetAssignedServices(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        List<ServiceCompany> assignments = await _serviceCompanyHandler.GetForCompany(organizationId, companyId);
        return assignments
            .OrderBy(sc => sc.Service.SortOrder)
            .ThenBy(sc => sc.Service.Name)
            .Select(sc => ToServiceDto(sc.Service))
            .ToList();
    }

    public async Task<List<CompanyDto>> ReplaceAssignedCompanies(Guid organizationId, Guid userId, Guid serviceId, ReplaceServiceCompaniesRequest request)
    {
        ServiceEntity service = await EnsureServiceExists(organizationId, serviceId);

        List<Guid> requestedIds = (request.CompanyIds ?? new List<Guid>()).Distinct().ToList();

        // GetByIds vraća samo Company koje pripadaju CurrentOrganizationId — tuđi/nepostojeći ID jednostavno
        // izostane iz rezultata, pa nedostajući broj otkriva cross-tenant/nevažeći ID (ponaša se kao NotFound,
        // isto kao pri direktnom pristupu tuđem resursu).
        List<Company> requestedCompanies = requestedIds.Count > 0
            ? await _companyHandler.GetByIds(organizationId, requestedIds)
            : new List<Company>();

        if (requestedCompanies.Count != requestedIds.Count)
        {
            HashSet<Guid> foundIds = requestedCompanies.Select(c => c.Id.GetValueOrDefault()).ToHashSet();
            Guid missingId = requestedIds.First(id => !foundIds.Contains(id));
            throw new NotFoundAppException("Company", missingId);
        }

        List<ServiceCompany> existingAssignments = await _serviceCompanyHandler.GetForService(organizationId, serviceId);
        HashSet<Guid> existingIds = existingAssignments.Select(sc => sc.CompanyId).ToHashSet();

        // Grandfathering: samo NOVE dodjele (nisu već u existingIds) zahtijevaju aktivnog Service/Company.
        // Zadržavanje postojeće (npr. neaktivne) dodjele u requestedIds je uvijek dopušteno.
        List<Company> newAssignments = requestedCompanies.Where(c => !existingIds.Contains(c.Id.GetValueOrDefault())).ToList();
        if (newAssignments.Count > 0)
        {
            if (!service.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveService, $"Usluga '{service.Name}' nije aktivna — nova dostupnost po poslovnici se ne može dodati.");

            Company inactiveCompany = newAssignments.FirstOrDefault(c => !c.IsActive);
            if (inactiveCompany != null)
                throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{inactiveCompany.Name}' nije aktivna — usluga joj se ne može novo dodijeliti.");
        }

        await _serviceCompanyHandler.ReplaceForService(serviceId, requestedIds);

        return await GetAssignedCompanies(organizationId, serviceId);
    }

    public async Task<bool> IsServiceAvailableAtCompany(Guid organizationId, Guid serviceId, Guid companyId)
    {
        return await _serviceCompanyHandler.IsAvailable(organizationId, serviceId, companyId);
    }

    public async Task<List<AppointmentServiceOptionDto>> GetBookableServices(Guid organizationId, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        List<ServiceCompany> assignments = await _serviceCompanyHandler.GetForCompany(organizationId, companyId);
        return assignments
            .Where(sc => sc.Service.IsActive)
            .OrderBy(sc => sc.Service.SortOrder)
            .ThenBy(sc => sc.Service.Name)
            .Select(sc => ToServiceOptionDto(sc.Service))
            .ToList();
    }

    private async Task<ServiceEntity> EnsureServiceExists(Guid organizationId, Guid serviceId)
    {
        ServiceEntity service = await _serviceHandler.GetById(organizationId, serviceId);
        if (service == null)
            throw new NotFoundAppException("Service", serviceId);

        return service;
    }

    private static CompanyDto ToCompanyDto(Company company)
    {
        return new CompanyDto
        {
            Id = company.Id.GetValueOrDefault(),
            Name = company.Name,
            Address = company.Address,
            Phone = company.Phone,
            ColorHex = company.ColorHex,
            Country = company.Country,
            IsActive = company.IsActive,
            Note = company.Note,
            SortOrder = company.SortOrder,
            CreatedAt = company.CreatedAt,
            CreatedBy = company.CreatedBy,
            UpdatedAt = company.UpdatedAt,
            UpdatedBy = company.UpdatedBy
        };
    }

    private static ServiceDto ToServiceDto(ServiceEntity service)
    {
        return new ServiceDto
        {
            Id = service.Id.GetValueOrDefault(),
            Name = service.Name,
            ExecutionMode = service.ExecutionMode,
            ColorHex = service.ColorHex,
            DefaultDurationMinutes = service.DefaultDurationMinutes,
            DefaultPrice = service.DefaultPrice,
            Description = service.Description,
            IsActive = service.IsActive,
            SortOrder = service.SortOrder,
            CreatedAt = service.CreatedAt,
            CreatedBy = service.CreatedBy,
            UpdatedAt = service.UpdatedAt,
            UpdatedBy = service.UpdatedBy
        };
    }

    private static AppointmentServiceOptionDto ToServiceOptionDto(ServiceEntity service)
    {
        return new AppointmentServiceOptionDto
        {
            Id = service.Id.GetValueOrDefault(),
            Name = service.Name,
            ExecutionMode = service.ExecutionMode,
            ColorHex = service.ColorHex,
            DefaultDurationMinutes = service.DefaultDurationMinutes,
            DefaultPrice = service.DefaultPrice
        };
    }
}
