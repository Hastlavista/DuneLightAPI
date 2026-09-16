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

/// <summary>
/// Company == fizička poslovnica/lokacija (npr. "Studio Zagreb", "Studio Split"), ne pravna tvrtka. Svaka
/// pripada točno jednoj Organization (tenant granica) i OrganizationId se nakon kreiranja više ne mijenja —
/// ovaj servis namjerno nikad ne dira to polje u Update/SetActive.
/// </summary>
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
        string name = request.Name?.Trim();
        await EnsureNameIsUnique(organizationId, name, excludeId: null);

        Company company = new Company
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Address = request.Address,
            Phone = request.Phone,
            ColorHex = request.ColorHex,
            Country = request.Country,
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

        string name = request.Name?.Trim();
        await EnsureNameIsUnique(organizationId, name, excludeId: id);

        // OrganizationId i Id se namjerno ne diraju — Company nikad ne mijenja vlasničku organizaciju.
        company.Name = name;
        company.Address = request.Address;
        company.Phone = request.Phone;
        company.ColorHex = request.ColorHex;
        company.Country = request.Country;
        company.Note = request.Note;
        company.SortOrder = request.SortOrder;
        company.UpdatedAt = DateTimeOffset.UtcNow;
        company.UpdatedBy = userId;

        await _companyHandler.Update(company);
        return ToDto(company);
    }

    public async Task<CompanyDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        if (isActive)
            return await Reactivate(organizationId, userId, id);

        return await Deactivate(organizationId, userId, id);
    }

    private async Task<CompanyDto> Reactivate(Guid organizationId, Guid userId, Guid id)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        if (!company.IsActive)
        {
            // Naziv je mogao u međuvremenu "procuriti" na drugu aktivnu tvrtku dok je ova bila neaktivna
            // (djelomični unique indeks vrijedi samo WHERE is_active = true) — provjeri prije povratka u pogon.
            await EnsureNameIsUnique(organizationId, company.Name, excludeId: id);

            company.IsActive = true;
            company.UpdatedAt = DateTimeOffset.UtcNow;
            company.UpdatedBy = userId;
            await _companyHandler.Update(company);
        }

        return ToDto(company);
    }

    private async Task<CompanyDto> Deactivate(Guid organizationId, Guid userId, Guid id)
    {
        CompanyDeactivationOutcome outcome = await _companyHandler.Deactivate(organizationId, id, userId);

        switch (outcome)
        {
            case CompanyDeactivationOutcome.NotFound:
                throw new NotFoundAppException("Company", id);
            case CompanyDeactivationOutcome.Blocked:
                throw new BusinessRuleException(ErrorCodes.LastActiveCompany, "Mora postojati barem jedna aktivna tvrtka.");
            case CompanyDeactivationOutcome.AlreadyInactive:
            case CompanyDeactivationOutcome.Deactivated:
                return await GetById(organizationId, id);
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Company company = await _companyHandler.GetById(organizationId, id);
        if (company == null)
            throw new NotFoundAppException("Company", id);

        bool isReferenced = await _companyHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Tvrtka je korištena u poslovnim podacima i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _companyHandler.Delete(company);
    }

    private async Task EnsureNameIsUnique(Guid organizationId, string name, Guid? excludeId)
    {
        bool exists = await _companyHandler.NameExistsAmongActive(organizationId, name, excludeId);
        if (exists)
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivna tvrtka s nazivom '{name}' već postoji.");
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
}
