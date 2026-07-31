using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class EmployeeHandler : IEmployeeHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public EmployeeHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<(List<Employee> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, Guid? engagementTypeId, UserRole? role)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Employee> query = context.Employees
            .Include(e => e.EngagementType)
            .Include(e => e.User)
            .Include(e => e.Locations).ThenInclude(el => el.Location)
            .Include(e => e.Services).ThenInclude(es => es.Service)
            .Where(e => e.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(e =>
                EF.Functions.ILike(e.FirstName, $"%{request.Search}%") ||
                EF.Functions.ILike(e.LastName, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(e => e.IsActive == request.IsActive.Value);

        if (locationId.HasValue)
            query = query.Where(e => e.Locations.Any(el => el.LocationId == locationId.Value));

        if (engagementTypeId.HasValue)
            query = query.Where(e => e.EngagementTypeId == engagementTypeId.Value);

        if (role.HasValue)
            query = query.Where(e => e.User.Role == role.Value);

        int totalCount = await query.CountAsync();

        List<Employee> items = await query
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Employee> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees
            .Include(e => e.EngagementType)
            .Include(e => e.User)
            .Include(e => e.Locations).ThenInclude(el => el.Location)
            .Include(e => e.Services).ThenInclude(es => es.Service)
            .SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
    }

    public async Task<Employee> GetByIdLight(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees
            .SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == id);
    }

    public async Task Add(Employee employee)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();
    }

    public async Task AddWithLogin(User user, Employee employee)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Users.Add(user);
        context.Employees.Add(employee);
        await context.SaveChangesAsync();
    }

    public async Task Update(Employee employee, List<EmployeeLocation> newLocations, List<EmployeeServiceAssignment> newServices)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        Employee trackedEmployee = await context.Employees.SingleAsync(e => e.Id == employee.Id && e.OrganizationId == employee.OrganizationId);
        trackedEmployee.FirstName = employee.FirstName;
        trackedEmployee.LastName = employee.LastName;
        trackedEmployee.Phone = employee.Phone;
        trackedEmployee.Email = employee.Email;
        trackedEmployee.DateOfBirth = employee.DateOfBirth;
        trackedEmployee.Address = employee.Address;
        trackedEmployee.Oib = employee.Oib;
        trackedEmployee.Note = employee.Note;
        trackedEmployee.CompensationNote = employee.CompensationNote;
        trackedEmployee.ColorHex = employee.ColorHex;
        trackedEmployee.SortOrder = employee.SortOrder;
        trackedEmployee.EmploymentStartDate = employee.EmploymentStartDate;
        trackedEmployee.EmploymentEndDate = employee.EmploymentEndDate;
        trackedEmployee.EngagementTypeId = employee.EngagementTypeId;
        trackedEmployee.IsActive = employee.IsActive;
        trackedEmployee.UpdatedAt = employee.UpdatedAt;
        trackedEmployee.UpdatedBy = employee.UpdatedBy;

        // Reconcile in place rather than delete-all/insert-all: recreating a row for a
        // location/service that didn't change would delete and re-insert the same
        // (employee_id, location_id) / (employee_id, service_id) pair in one batch, which
        // can violate the unique indexes depending on statement ordering within the batch.
        List<EmployeeLocation> existingLocations = await context.EmployeeLocations
            .Where(el => el.EmployeeId == employee.Id)
            .ToListAsync();
        Dictionary<Guid, EmployeeLocation> existingLocationsByLocationId = existingLocations.ToDictionary(el => el.LocationId);
        HashSet<Guid> newLocationIds = newLocations.Select(l => l.LocationId).ToHashSet();

        foreach (EmployeeLocation existing in existingLocations)
            if (!newLocationIds.Contains(existing.LocationId))
                context.EmployeeLocations.Remove(existing);

        foreach (EmployeeLocation location in newLocations)
        {
            if (existingLocationsByLocationId.TryGetValue(location.LocationId, out EmployeeLocation existing))
                existing.IsPrimary = location.IsPrimary;
            else
            {
                location.EmployeeId = employee.Id.GetValueOrDefault();
                context.EmployeeLocations.Add(location);
            }
        }

        List<EmployeeServiceAssignment> existingServices = await context.EmployeeServiceAssignments
            .Where(es => es.EmployeeId == employee.Id)
            .ToListAsync();
        HashSet<Guid> existingServiceIds = existingServices.Select(es => es.ServiceId).ToHashSet();
        HashSet<Guid> newServiceIds = newServices.Select(s => s.ServiceId).ToHashSet();

        foreach (EmployeeServiceAssignment existing in existingServices)
            if (!newServiceIds.Contains(existing.ServiceId))
                context.EmployeeServiceAssignments.Remove(existing);

        foreach (EmployeeServiceAssignment service in newServices)
            if (!existingServiceIds.Contains(service.ServiceId))
            {
                service.EmployeeId = employee.Id.GetValueOrDefault();
                context.EmployeeServiceAssignments.Add(service);
            }

        await context.SaveChangesAsync();
    }

    public async Task SetActiveWithLogin(Guid organizationId, Guid employeeId, Guid userId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        Employee employee = await context.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.OrganizationId == organizationId);
        if (employee == null)
            throw new ArgumentException($"Employee with id {employeeId} does not exist");

        employee.IsActive = isActive;
        employee.UpdatedAt = updatedAt;
        employee.UpdatedBy = updatedBy;

        User user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId);
        if (user == null)
            throw new ArgumentException($"User with id {userId} does not exist");

        user.IsActive = isActive;

        await context.SaveChangesAsync();
    }

    public async Task DeleteWithLoginDeactivation(Employee employee)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        context.Employees.Remove(employee);

        User user = await context.Users.SingleOrDefaultAsync(u => u.Id == employee.UserId && u.OrganizationId == employee.OrganizationId);
        if (user != null)
            user.IsActive = false;

        await context.SaveChangesAsync();
    }

    public async Task<Employee> GetByUserId(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees.SingleOrDefaultAsync(e =>
            e.OrganizationId == organizationId && e.UserId == userId);
    }

    public async Task<bool> IsUserAlreadyLinked(Guid organizationId, Guid userId, Guid? excludeEmployeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees.AnyAsync(e =>
            e.OrganizationId == organizationId &&
            e.UserId == userId &&
            (excludeEmployeeId == null || e.Id != excludeEmployeeId));
    }

    public async Task<int> CountActiveAdmins(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Employees.CountAsync(e =>
            e.OrganizationId == organizationId &&
            e.IsActive &&
            e.User.Role == UserRole.Admin);
    }

    public async Task<(List<EmployeeDirectoryDto> Items, int TotalCount)> GetDirectoryPaged(Guid organizationId, PagedRequest request)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<Employee> query = context.Employees.Where(e => e.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(e =>
                EF.Functions.ILike(e.FirstName, $"%{request.Search}%") ||
                EF.Functions.ILike(e.LastName, $"%{request.Search}%"));

        if (request.IsActive.HasValue)
            query = query.Where(e => e.IsActive == request.IsActive.Value);

        int totalCount = await query.CountAsync();

        List<EmployeeDirectoryDto> items = await query
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(e => new EmployeeDirectoryDto
            {
                Id = e.Id.GetValueOrDefault(),
                FirstName = e.FirstName,
                LastName = e.LastName,
                ColorHex = e.ColorHex,
                IsActive = e.IsActive,
                Locations = e.Locations.Select(el => el.Location.Name).ToList()
            })
            .ToListAsync();

        return (items, totalCount);
    }
}
