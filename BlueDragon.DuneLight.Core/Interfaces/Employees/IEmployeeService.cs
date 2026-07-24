using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;

namespace BlueDragon.DuneLight.Core.Interfaces.Employees;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, Guid? engagementTypeId, UserRole? role);

    Task<EmployeeDto> GetById(Guid organizationId, Guid id);
    Task<EmployeeDto> Create(Guid organizationId, Guid userId, EmployeeCreateRequest request);

    /// <summary>Stvara korisnika (unutar organizacije prijavljenog admina) i zaposlenika zajedno, atomično.</summary>
    Task<EmployeeWithLoginCreateResponse> CreateWithLogin(Guid organizationId, Guid currentUserId, EmployeeWithLoginCreateRequest request);
    Task<EmployeeDto> Update(Guid organizationId, Guid userId, Guid id, EmployeeUpdateRequest request);
    Task<EmployeeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);
    Task Delete(Guid organizationId, Guid id);
    Task<EmployeeDto> UpdateRole(Guid organizationId, Guid userId, Guid id, UserRole newRole);

    /// <summary>Ograničeni pogled za trenere/recepciju — zaseban DTO, ne filtriranje punog EmployeeDto.</summary>
    Task<PagedResult<EmployeeDirectoryDto>> GetDirectory(Guid organizationId, PagedRequest request);

    /// <summary>Zaposlenik povezan s prijavljenim korisnikom ("tko sam ja"). Baca NotFoundAppException ako korisnik nema povezanog zaposlenika.</summary>
    Task<EmployeeMeDto> GetMe(Guid organizationId, Guid userId);
}
