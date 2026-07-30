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
    private readonly ILocationHandler _locationHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IAuthHandler _authHandler;
    private readonly IEmployeeAuditLogHandler _auditLogHandler;
    private readonly IFutureAppointmentsProvider _futureAppointmentsProvider;

    public EmployeeService(
        IEmployeeHandler employeeHandler,
        IEngagementTypeHandler engagementTypeHandler,
        ILocationHandler locationHandler,
        IServiceHandler serviceHandler,
        IAuthHandler authHandler,
        IEmployeeAuditLogHandler auditLogHandler,
        IFutureAppointmentsProvider futureAppointmentsProvider)
    {
        _employeeHandler = employeeHandler;
        _engagementTypeHandler = engagementTypeHandler;
        _locationHandler = locationHandler;
        _serviceHandler = serviceHandler;
        _authHandler = authHandler;
        _auditLogHandler = auditLogHandler;
        _futureAppointmentsProvider = futureAppointmentsProvider;
    }

    public async Task<PagedResult<EmployeeDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, Guid? engagementTypeId, UserRole? role)
    {
        (List<Employee> items, int totalCount) = await _employeeHandler.GetPaged(organizationId, request, locationId, engagementTypeId, role);
        return PagedResult<EmployeeDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<EmployeeDto> GetById(Guid organizationId, Guid id)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        return ToDto(employee);
    }

    public async Task<EmployeeDto> Create(Guid organizationId, Guid userId, EmployeeCreateRequest request)
    {
        ValidateLocations(request.LocationIds, request.PrimaryLocationId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);
        await EnsureLocationsUsable(organizationId, request.LocationIds, grandfatheredLocationIds: null);
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

        employee.Locations = BuildLocations(request.LocationIds, request.PrimaryLocationId);
        employee.Services = BuildServices(request.ServiceIds);

        await _employeeHandler.Add(employee);
        return await GetById(organizationId, employee.Id.GetValueOrDefault());
    }

    public async Task<EmployeeWithLoginCreateResponse> CreateWithLogin(Guid organizationId, Guid currentUserId, EmployeeWithLoginCreateRequest request)
    {
        ValidateLocations(request.LocationIds, request.PrimaryLocationId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);
        await EnsureLocationsUsable(organizationId, request.LocationIds, grandfatheredLocationIds: null);
        await EnsureEngagementTypeIsUsable(organizationId, request.EngagementTypeId);
        await EnsureServicesUsable(organizationId, request.ServiceIds, grandfatheredServiceIds: null);

        bool emailExists = await _authHandler.EmailExists(organizationId, request.Email);
        if (emailExists)
            throw new BusinessRuleException(ErrorCodes.EmailAlreadyInUse, "Korisnik s ovom email adresom već postoji u organizaciji.");

        User user = new User
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = request.Email,
            PasswordHash = PasswordHasher.Hash(request.Password),
            ApiKey = Guid.NewGuid().ToString("N"),
            Role = request.Role,
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
            Locations = BuildLocations(request.LocationIds, request.PrimaryLocationId),
            Services = BuildServices(request.ServiceIds)
        };

        await _employeeHandler.AddWithLogin(user, employee);

        return new EmployeeWithLoginCreateResponse
        {
            EmployeeId = employee.Id.GetValueOrDefault(),
            UserId = user.Id.GetValueOrDefault(),
            Email = user.Email,
            Role = UserRoleClaims.ToClaimValue(user.Role)
        };
    }

    public async Task<EmployeeDto> Update(Guid organizationId, Guid userId, Guid id, EmployeeUpdateRequest request)
    {
        Employee existing = await _employeeHandler.GetById(organizationId, id);
        if (existing == null)
            throw new NotFoundAppException("Employee", id);

        ValidateLocations(request.LocationIds, request.PrimaryLocationId);
        ValidateEmploymentDates(request.EmploymentStartDate, request.EmploymentEndDate);

        HashSet<Guid> grandfatheredLocationIds = existing.Locations.Select(el => el.LocationId).ToHashSet();
        HashSet<Guid> grandfatheredServiceIds = existing.Services.Select(es => es.ServiceId).ToHashSet();

        await EnsureLocationsUsable(organizationId, request.LocationIds, grandfatheredLocationIds);
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

        List<EmployeeLocation> newLocations = BuildLocations(request.LocationIds, request.PrimaryLocationId);
        List<EmployeeServiceAssignment> newServices = BuildServices(request.ServiceIds);

        await _employeeHandler.Update(existing, newLocations, newServices);
        return await GetById(organizationId, id);
    }

    public async Task<EmployeeDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        if (isActive == employee.IsActive)
            return ToDto(employee);

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
        await _employeeHandler.SetActiveAndStamp(id, isActive, now, userId);
        await _authHandler.SetActive(employee.UserId, isActive);

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

        await _employeeHandler.Delete(employee);
        await _authHandler.SetActive(employee.UserId, false);
    }

    public async Task<EmployeeDto> UpdateRole(Guid organizationId, Guid userId, Guid id, UserRole newRole)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, id);
        if (employee == null)
            throw new NotFoundAppException("Employee", id);

        UserRole oldRole = employee.User.Role;
        if (oldRole == newRole)
            return ToDto(employee);

        if (oldRole == UserRole.Admin && newRole != UserRole.Admin)
        {
            int activeAdmins = await _employeeHandler.CountActiveAdmins(organizationId);
            if (activeAdmins <= 1)
                throw new BusinessRuleException(ErrorCodes.LastActiveAdmin, "Mora postojati barem jedan aktivan Admin — nije moguće promijeniti ulogu zadnjeg.");
        }

        await _authHandler.UpdateRole(employee.UserId, newRole);

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
        return new EmployeeMeDto
        {
            EmployeeId = full.Id.GetValueOrDefault(),
            FirstName = full.FirstName,
            LastName = full.LastName,
            Role = full.User != null ? UserRoleClaims.ToClaimValue(full.User.Role) : null,
            ColorHex = full.ColorHex,
            Locations = full.Locations.Select(el => new EmployeeLocationDto
            {
                LocationId = el.LocationId,
                LocationName = el.Location?.Name,
                IsPrimary = el.IsPrimary
            }).ToList()
        };
    }

    private static void ValidateLocations(List<Guid> locationIds, Guid primaryLocationId)
    {
        if (locationIds == null || locationIds.Count == 0)
            throw new ValidationAppException("Zaposlenik mora imati barem jednu lokaciju.");

        if (!locationIds.Contains(primaryLocationId))
            throw new ValidationAppException("Matična lokacija mora biti među dodijeljenim lokacijama.");
    }

    private static void ValidateEmploymentDates(DateTimeOffset start, DateTimeOffset? end)
    {
        if (end.HasValue && end.Value < start)
            throw new ValidationAppException("Datum prestanka ne smije biti prije datuma početka.");
    }

    private async Task EnsureLocationsUsable(Guid organizationId, List<Guid> locationIds, HashSet<Guid> grandfatheredLocationIds)
    {
        foreach (Guid locationId in locationIds.Distinct())
        {
            Location location = await _locationHandler.GetById(organizationId, locationId);
            if (location == null)
                throw new NotFoundAppException("Location", locationId);

            bool isGrandfathered = grandfatheredLocationIds != null && grandfatheredLocationIds.Contains(locationId);
            if (!location.IsActive && !isGrandfathered)
                throw new ValidationAppException($"Odabrana lokacija '{location.Name}' nije aktivna.");
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

        foreach (Guid serviceId in serviceIds.Distinct())
        {
            Service service = await _serviceHandler.GetById(organizationId, serviceId);
            if (service == null)
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

    private static List<EmployeeLocation> BuildLocations(List<Guid> locationIds, Guid primaryLocationId)
    {
        return locationIds.Distinct().Select(locationId => new EmployeeLocation
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            IsPrimary = locationId == primaryLocationId
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

    private static EmployeeDto ToDto(Employee employee)
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
            Locations = employee.Locations.Select(el => new EmployeeLocationDto
            {
                LocationId = el.LocationId,
                LocationName = el.Location?.Name,
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
}
