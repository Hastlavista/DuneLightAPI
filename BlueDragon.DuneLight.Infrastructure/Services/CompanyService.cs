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

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyHandler _companyHandler;

    public CompanyService(ICompanyHandler companyHandler)
    {
        _companyHandler = companyHandler;
    }

    public async Task<PagedResult<CompanyDto>> GetPaged(Guid organizationId, PagedRequest request)
    {
        (List<Company> items, int totalCount) = await _companyHandler.GetPaged(organizationId, request);
        return PagedResult<CompanyDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<CompanyDto> GetById(Guid organizationId, Guid id)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        return ToDto(company);
    }

    public async Task<CompanyDto> Create(Guid organizationId, Guid userId, CompanyCreateRequest request)
    {
        Company company = new Company
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            Address = request.Address,
            Phone = request.Phone,
            ColorHex = request.ColorHex,
            Note = request.Note,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        await _companyHandler.Add(company);
        return ToDto(company);
    }

    public async Task<CompanyDto> Update(Guid organizationId, Guid userId, Guid id, CompanyUpdateRequest request)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        company.Name = request.Name;
        company.Address = request.Address;
        company.Phone = request.Phone;
        company.ColorHex = request.ColorHex;
        company.Note = request.Note;
        company.SortOrder = request.SortOrder;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        company.UpdatedBy = userId;

        await _companyHandler.Update(company);
        return ToDto(company);
    }

    public async Task<CompanyDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        if (!isActive && company.IsActive)
        {
            int activeCount = await _companyHandler.CountActive(organizationId);
            if (activeCount <= 1)
                throw new BusinessRuleException(ErrorCodes.LastActiveCompany, "Mora postojati barem jedna aktivna tvrtka.");
        }

        company.IsActive = isActive;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        company.UpdatedBy = userId;

        await _companyHandler.Update(company);
        return ToDto(company);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        bool isReferenced = await _companyHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Tvrtka je korištena u cjeniku i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _companyHandler.Delete(company);
    }

    private static CompanyDto ToDto(Company company)
    {
        return new CompanyDto
        {
            Id = company.Id.GetValueOrDefault(),
            Name = company.Name,
            Address = company.Address,
            Phone = company.Phone,
            ColorHex = company.ColorHex,
            IsActive = company.IsActive,
            Note = company.Note,
            SortOrder = company.SortOrder,
            CreatedAt = company.CreatedAt,
            CreatedBy = company.CreatedBy,
            UpdatedAt = company.UpdatedAt,
            UpdatedBy = company.UpdatedBy
        };
    }
}
