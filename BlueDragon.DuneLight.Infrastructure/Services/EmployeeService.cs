using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Employees;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IEngagementTypeHandler _engagementTypeHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IAuthHandler _authHandler;
    private readonly IEmployeeAuditLogHandler _auditLogHandler;
    private readonly IFutureAppointmentsProvider _futureAppointmentsProvider;
    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly IRoleHandler _roleHandler;

    public EmployeeService(
        IEmployeeHandler employeeHandler,
        IEngagementTypeHandler engagementTypeHandler,
        ICompanyHandler companyHandler,
        IServiceHandler serviceHandler,
        IAuthHandler authHandler,
        IEmployeeAuditLogHandler auditLogHandler,
        IFutureAppointmentsProvider futureAppointmentsProvider,
        IGrantGroupHandler grantGroupHandler,
        IRoleHandler roleHandler)
    {
        _employeeHandler = employeeHandler;
        _engagementTypeHandler = engagementTypeHandler;
        _companyHandler = companyHandler;
        _serviceHandler = serviceHandler;
        _authHandler = authHandler;
        _auditLogHandler = auditLogHandler;
        _futureAppointmentsProvider = futureAppointmentsProvider;
        _grantGroupHandler = grantGroupHandler;
        _roleHandler = roleHandler;
    }

    public async Task<PagedResult<EmployeeDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? companyId, Guid? engagementTypeId, UserRole? role)
    {
        (List<Employee> items, int totalCount) = await _employeeHandler.GetPaged(organizationId, request, companyId, engagementTypeId, role);

        List<Guid> userIds = items.Select(e => e.UserId).Distinct().ToList();
        Dictionary<Guid, List<string>> grantGroupNamesByUserId = await _grantGroupHandler.GetGrantGroupNamesByUserIds(organizationId, userIds);
        Dictionary<Guid, List<string>> roleNamesByUserId = await _roleHandler.GetRoleNamesByUserIds(organizationId, userIds);

        List<EmployeeDto> dtos = items
            .Select(e => ToDto(
                e,
                grantGroupNamesByUserId.GetValueOrDefault(e.UserId, new List<string>()),
                roleNamesByUserId.GetValueOrDefault(e.UserId, new List<string>())))
            .ToList();

        return PagedResult<EmployeeDto>.Create(dtos, totalCount, request.Page, request.PageSize);
    }

    public async Task<EmployeeDto> GetById(Guid organizationId, Guid id)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        return await ToDtoSingle(organizationId, employee);
    }

    public async Task<EmployeeDto> Create(Guid organizationId, Guid userId, EmployeeCreateRequest request)
    {
        ValidateCompanies(request.CompanyIds, request.PrimaryCompanyId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);
        await EnsureCompaniesUsable(organizationId, request.CompanyIds, grandfatheredCompanyIds: null);
        await EnsureEngagementTypeIsUsable(organizationId, request.EngagementTypeId);
        await EnsureServicesUsable(organizationId, request.ServiceIds, grandfatheredServiceIds: null);
        await EnsureUserIsLinkable(organizationId, request.UserId, excludeEmployeeId: null);

        Employee employee = new Employee
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            DateOfBirth = request.DateOfBirth,
            Address = request.Address,
            Oib = request.Oib,
            Note = request.Note,
            CompensationNote = request.CompensationNote,
            ColorHex = request.ColorHex,
            SortOrder = request.SortOrder,
            EmploymentStartDate = request.EmploymentStartDate,
            EmploymentEndDate = request.EmploymentEndDate,
            EngagementTypeId = request.EngagementTypeId,
            UserId = request.UserId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        employee.Companies = BuildCompanies(request.CompanyIds, request.PrimaryCompanyId);
        employee.Services = BuildServices(request.ServiceIds);

        await _employeeHandler.Add(employee);
        return await GetById(organizationId, employee.Id.GetValueOrDefault());
    }

    public async Task<EmployeeWithLoginCreateResponse> CreateWithLogin(Guid organizationId, Guid currentUserId, EmployeeWithLoginCreateRequest request)
    {
        ValidateCompanies(request.CompanyIds, request.PrimaryCompanyId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);
        await EnsureCompaniesUsable(organizationId, request.CompanyIds, grandfatheredCompanyIds: null);
        await EnsureEngagementTypeIsUsable(organizationId, request.EngagementTypeId);
        await EnsureServicesUsable(organizationId, request.ServiceIds, grandfatheredServiceIds: null);

        bool emailExists = await _authHandler.EmailExists(organizationId, request.Email);
        if (emailExists)
            throw new BusinessRuleException(ErrorCodes.EmailAlreadyInUse, "Korisnik s ovom email adresom već postoji u organizaciji.");

        await EnsureGrantGroupsUsable(organizationId, request.GrantGroupIds);
        await EnsureRolesUsable(organizationId, request.RoleIds);

        User user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = request.Email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            ApiKey = Guid.NewGuid().ToString("N"),
            // Legacy UserRole stupac se uklanja u sljedećem koraku migracije s grant sustava — do tada je
            // ovo samo kozmetička/tranzicijska vrijednost, autorizacija ide isključivo kroz GrantGroupIds.
            Role = UserRole.Member,
            MustChangeCredentialsOnFirstLogin = request.MustChangeCredentialsOnFirstLogin,
            PinHash = string.IsNullOrEmpty(request.Pin) ? null : PasswordHasher.Hash(request.Pin),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Employee employee = new Employee
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            DateOfBirth = request.DateOfBirth,
            Address = request.Address,
            Oib = request.Oib,
            Note = request.Note,
            CompensationNote = request.CompensationNote,
            ColorHex = request.ColorHex,
            SortOrder = request.SortOrder,
            EmploymentStartDate = request.EmploymentStartDate,
            EmploymentEndDate = request.EmploymentEndDate,
            EngagementTypeId = request.EngagementTypeId,
            UserId = user.Id.GetValueOrDefault(),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = currentUserId,
            Companies = BuildCompanies(request.CompanyIds, request.PrimaryCompanyId),
            Services = BuildServices(request.ServiceIds)
        };

        await _employeeHandler.AddWithLogin(user, employee);

        await _grantGroupHandler.SetUserGrantGroups(organizationId, user.Id.GetValueOrDefault(), request.GrantGroupIds);
        await _roleHandler.SetUserRoles(organizationId, user.Id.GetValueOrDefault(), request.RoleIds ?? new List<Guid>());

        return new EmployeeWithLoginCreateResponse
        {
            EmployeeId = employee.Id.GetValueOrDefault(),
            UserId = user.Id.GetValueOrDefault(),
            Email = user.Email,
            GrantGroupIds = request.GrantGroupIds
        };
    }

    private async Task EnsureGrantGroupsUsable(Guid organizationId, List<Guid> grantGroupIds)
    {
        List<Guid> distinctIds = grantGroupIds.Distinct().ToList();
        HashSet<Guid> validIds = (await _grantGroupHandler.GetAll(organizationId))
            .Select(g => g.Id.GetValueOrDefault())
            .ToHashSet();

        foreach (Guid grantGroupId in distinctIds)
            if (!validIds.Contains(grantGroupId))
                throw new NotFoundAppException("GrantGroup", grantGroupId);
    }

    private async Task EnsureRolesUsable(Guid organizationId, List<Guid> roleIds)
    {
        if (roleIds == null || roleIds.Count == 0)
            return;

        HashSet<Guid> validIds = (await _roleHandler.GetAll(organizationId))
            .Select(r => r.Id.GetValueOrDefault())
            .ToHashSet();

        foreach (Guid roleId in roleIds.Distinct())
            if (!validIds.Contains(roleId))
                throw new NotFoundAppException("Role", roleId);
    }

    public async Task<EmployeeDto> Update(Guid organizationId, Guid userId, Guid id, EmployeeUpdateRequest request)
    {
        Employee existing = await _employeeHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Employee", id);

        ValidateCompanies(request.CompanyIds, request.PrimaryCompanyId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);

        HashSet<Guid> grandfatheredCompanyIds = existing.Companies.Select(el => el.CompanyId).ToHashSet();
        HashSet<Guid> grandfatheredServiceIds = existing.Services.Select(es => es.ServiceId).ToHashSet();

        await EnsureCompaniesUsable(organizationId, request.CompanyIds, grandfatheredCompanyIds);
        await EnsureEngagementTypeIsUsable(organizationId, request.EngagementTypeId);
        await EnsureServicesUsable(organizationId, request.ServiceIds, grandfatheredServiceIds);

        existing.FirstName = request.FirstName;
        existing.LastName = request.LastName;
        existing.Phone = request.Phone;
        existing.Email = request.Email;
        existing.DateOfBirth = request.DateOfBirth;
        existing.Address = request.Address;
        existing.Oib = request.Oib;
        existing.Note = request.Note;
        existing.CompensationNote = request.CompensationNote;
        existing.ColorHex = request.ColorHex;
        existing.SortOrder = request.SortOrder;
        existing.EmploymentStartDate = request.EmploymentStartDate;
        existing.EmploymentEndDate = request.EmploymentEndDate;
        existing.EngagementTypeId = request.EngagementTypeId;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.UpdatedBy = userId;

        List<EmployeeCompany> newCompanies = BuildCompanies(request.CompanyIds, request.PrimaryCompanyId);
        List<EmployeeServiceAssignment> newServices = BuildServices(request.ServiceIds);

        await _employeeHandler.Update(existing, newCompanies, newServices);
        return await GetById(organizationId, id);
    }

    public async Task<EmployeeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        if (isActive == employee.IsActive)
            return await ToDtoSingle(organizationId, employee);

        if (!isActive)
        {
            if (employee.User.Role == UserRole.Admin)
            {
                int activeAdmins = await _employeeHandler.CountActiveAdmins(organizationId);
                if (activeAdmins <= 1)
                    throw new BusinessRuleException(ErrorCodes.LastActiveAdmin, "Mora postojati barem jedan aktivan Admin — nije moguće deaktivirati zadnjeg.");
            }
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await _employeeHandler.SetActiveWithLogin(organizationId, id, employee.UserId, isActive, now, userId);

        await _auditLogHandler.Add(new EmployeeAuditLog
        {
            Id = Guid.NewGuid(),
            EmployeeId = id,
            ChangeType = "Status",
            OldValue = employee.IsActive ? "Active" : "Inactive",
            NewValue = isActive ? "Active" : "Inactive",
            ChangedAt = now,
            ChangedBy = userId
        });

        EmployeeDto result = await GetById(organizationId, id);

        if (!isActive)
        {
            bool hasFutureAppointments = await _futureAppointmentsProvider.HasFutureAppointments(organizationId, id);
            if (hasFutureAppointments)
                result.Warning = "Zaposlenik ima buduće termine.";
        }

        return result;
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        Employee employee = await _employeeHandler.GetByIdLight(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        bool hasHistory = await _auditLogHandler.HasEntries(id);
        bool hasFutureAppointments = await _futureAppointmentsProvider.HasFutureAppointments(organizationId, id);
        if (hasHistory || hasFutureAppointments)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Zaposlenik je referenciran (povijest promjena i/ili budući termini) i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

        await _employeeHandler.DeleteWithLoginDeactivation(employee);
    }

    public async Task<EmployeeDto> UpdateRole(Guid organizationId, Guid userId, Guid id, UserRole newRole)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        UserRole oldRole = employee.User.Role;
        if (oldRole == newRole)
            return await ToDtoSingle(organizationId, employee);

        if (oldRole == UserRole.Admin && newRole != UserRole.Admin)
        {
            int activeAdmins = await _employeeHandler.CountActiveAdmins(organizationId);
            if (activeAdmins <= 1)
                throw new BusinessRuleException(ErrorCodes.LastActiveAdmin, "Mora postojati barem jedan aktivan Admin — nije moguće promijeniti ulogu zadnjeg.");
        }

        await _authHandler.UpdateRole(organizationId, employee.UserId, newRole);

        await _auditLogHandler.Add(new EmployeeAuditLog
        {
            Id = Guid.NewGuid(),
            EmployeeId = id,
            ChangeType = "Role",
            OldValue = UserRoleClaims.ToClaimValue(oldRole),
            NewValue = UserRoleClaims.ToClaimValue(newRole),
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });

        return await GetById(organizationId, id);
    }

    public async Task<PagedResult<EmployeeDirectoryDto>> GetDirectory(Guid organizationId, PagedRequest request)
    {
        (List<EmployeeDirectoryDto> items, int totalCount) = await _employeeHandler.GetDirectoryPaged(organizationId, request);
        return PagedResult<EmployeeDirectoryDto>.Create(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<EmployeeMeDto> GetMe(Guid organizationId, Guid userId)
    {
        Employee employee = await _employeeHandler.GetByUserId(organizationId, userId);
        if (employee == null)
            throw new NotFoundAppException("Employee", userId);

        Employee full = await _employeeHandler.GetById(organizationId, employee.Id.GetValueOrDefault());
        (bool isOwner, HashSet<string> grants) = await _grantGroupHandler.ResolveEffective(organizationId, userId);

        return new EmployeeMeDto
        {
            EmployeeId = full.Id.GetValueOrDefault(),
            FirstName = full.FirstName,
            LastName = full.LastName,
            Role = full.User != null ? UserRoleClaims.ToClaimValue(full.User.Role) : null,
            IsOwner = isOwner,
            Grants = grants.ToList(),
            ColorHex = full.ColorHex,
            Companies = full.Companies.Select(el => new EmployeeCompanyDto
            {
                CompanyId = el.CompanyId,
                CompanyName = el.Company?.Name,
                IsPrimary = el.IsPrimary
            }).ToList()
        };
    }

    private static void ValidateCompanies(List<Guid> companyIds, Guid primaryCompanyId)
    {
        if (companyIds == null || companyIds.Count == 0)
            throw new ValidationAppException("Zaposlenik mora imati barem jednu tvrtku.");

        if (!companyIds.Contains(primaryCompanyId))
            throw new ValidationAppException("Matična tvrtka mora biti među dodijeljenim tvrtkama.");
    }

    private static void ValidateEmploymentDates(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end.HasValue && end.Value < start)
            throw new ValidationAppException("Datum prestanka ne smije biti prije datuma početka.");
    }

    private async Task EnsureCompaniesUsable(Guid organizationId, List<Guid> companyIds, HashSet<Guid> grandfatheredCompanyIds)
    {
        List<Guid> distinctIds = companyIds.Distinct().ToList();
        Dictionary<Guid, Company> byId = (await _companyHandler.GetByIds(organizationId, distinctIds))
            .ToDictionary(l => l.Id.GetValueOrDefault());

        foreach (Guid companyId in distinctIds)
        {
            if (!byId.TryGetValue(companyId, out Company company))
                throw new NotFoundAppException("Company", companyId);

            bool isGrandfathered = grandfatheredCompanyIds != null && grandfatheredCompanyIds.Contains(companyId);
            if (!company.IsActive && !isGrandfathered)
                throw new ValidationAppException($"Odabrana tvrtka '{company.Name}' nije aktivna.");
        }
    }

    private async Task EnsureEngagementTypeIsUsable(Guid organizationId, Guid engagementTypeId)
    {
        EngagementType engagementType = await _engagementTypeHandler.GetById(organizationId, engagementTypeId);
        if (engagementType == null)
            throw new NotFoundAppException("EngagementType", engagementTypeId);

        if (!engagementType.IsActive)
            throw new ValidationAppException("Odabrana vrsta angažmana nije aktivna.");
    }

    private async Task EnsureServicesUsable(Guid organizationId, List<Guid> serviceIds, HashSet<Guid> grandfatheredServiceIds)
    {
        if (serviceIds == null)
            return;

        List<Guid> distinctIds = serviceIds.Distinct().ToList();
        Dictionary<Guid, Service> byId = (await _serviceHandler.GetByIds(organizationId, distinctIds))
            .ToDictionary(s => s.Id.GetValueOrDefault());

        foreach (Guid serviceId in distinctIds)
        {
            if (!byId.TryGetValue(serviceId, out Service service))
                throw new NotFoundAppException("Service", serviceId);

            bool isGrandfathered = grandfatheredServiceIds != null && grandfatheredServiceIds.Contains(serviceId);
            if (!service.IsActive && !isGrandfathered)
                throw new ValidationAppException($"Odabrana usluga '{service.Name}' nije aktivna.");
        }
    }

    private async Task EnsureUserIsLinkable(Guid organizationId, Guid requestedUserId, Guid? excludeEmployeeId)
    {
        User user = await _authHandler.GetUserById(requestedUserId);
        if (user == null || user.OrganizationId != organizationId)
            throw new NotFoundAppException("User", requestedUserId);

        bool alreadyLinked = await _employeeHandler.IsUserAlreadyLinked(organizationId, requestedUserId, excludeEmployeeId);
        if (alreadyLinked)
            throw new BusinessRuleException(ErrorCodes.UserAlreadyLinked, "Odabrani korisnički račun je već povezan s drugim zaposlenikom.");
    }

    private static List<EmployeeCompany> BuildCompanies(List<Guid> companyIds, Guid primaryCompanyId)
    {
        return companyIds.Distinct().Select(companyId => new EmployeeCompany
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            IsPrimary = companyId == primaryCompanyId
        }).ToList();
    }

    private static List<EmployeeServiceAssignment> BuildServices(List<Guid> serviceIds)
    {
        if (serviceIds == null)
            return new List<EmployeeServiceAssignment>();

        return serviceIds.Distinct().Select(serviceId => new EmployeeServiceAssignment
        {
            Id = Guid.NewGuid(),
            ServiceId = serviceId
        }).ToList();
    }

    private static EmployeeDto ToDto(Employee employee, List<string> grantGroupNames, List<string> roleNames)
    {
        return new EmployeeDto
        {
            Id = employee.Id.GetValueOrDefault(),
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            Phone = employee.Phone,
            Email = employee.Email,
            DateOfBirth = employee.DateOfBirth,
            Address = employee.Address,
            Oib = employee.Oib,
            Note = employee.Note,
            CompensationNote = employee.CompensationNote,
            ColorHex = employee.ColorHex,
            SortOrder = employee.SortOrder,
            EmploymentStartDate = employee.EmploymentStartDate,
            EmploymentEndDate = employee.EmploymentEndDate,
            EngagementTypeId = employee.EngagementTypeId,
            EngagementTypeName = employee.EngagementType?.Name,
            IsActive = employee.IsActive,
            UserId = employee.UserId,
            Role = employee.User != null ? UserRoleClaims.ToClaimValue(employee.User.Role) : null,
            GrantGroupNames = grantGroupNames,
            RoleNames = roleNames,
            Companies = employee.Companies.Select(el => new EmployeeCompanyDto
            {
                CompanyId = el.CompanyId,
                CompanyName = el.Company?.Name,
                IsPrimary = el.IsPrimary
            }).ToList(),
            Services = employee.Services.Select(es => new EmployeeServiceDto
            {
                ServiceId = es.ServiceId,
                ServiceName = es.Service?.Name
            }).ToList(),
            CreatedAt = employee.CreatedAt,
            CreatedBy = employee.CreatedBy,
            UpdatedAt = employee.UpdatedAt,
            UpdatedBy = employee.UpdatedBy
        };
    }

    /// <summary>Single-employee path (GetById and callers that short-circuit before it) - reuses the
    /// same bulk-friendly handler methods GetPaged uses, just with a one-element userId list, so there's
    /// only ever the one query shape to reason about.</summary>
    private async Task<EmployeeDto> ToDtoSingle(Guid organizationId, Employee employee)
    {
        List<Guid> userIds = new List<Guid> { employee.UserId };
        Dictionary<Guid, List<string>> grantGroupNamesByUserId = await _grantGroupHandler.GetGrantGroupNamesByUserIds(organizationId, userIds);
        Dictionary<Guid, List<string>> roleNamesByUserId = await _roleHandler.GetRoleNamesByUserIds(organizationId, userIds);

        return ToDto(
            employee,
            grantGroupNamesByUserId.GetValueOrDefault(employee.UserId, new List<string>()),
            roleNamesByUserId.GetValueOrDefault(employee.UserId, new List<string>()));
    }
}
