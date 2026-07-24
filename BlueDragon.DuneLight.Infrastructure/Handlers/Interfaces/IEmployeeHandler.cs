using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Employees;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IEmployeeHandler
{
    Task<(List<Employee> Items, int TotalCount)> GetPaged(
        Guid organizationId, PagedRequest request, Guid? locationId, Guid? engagementTypeId, UserRole? role);

    /// <summary>Puni graf (EngagementType, User, Locations, Services) — za prikaz/čitanje.</summary>
    Task<Employee> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije (izbjegava EF tracking sudar s Update/RemoveRange).</summary>
    Task<Employee> GetByIdLight(Guid organizationId, Guid id);

    Task Add(Employee employee);

    /// <summary>Stvara User i Employee u jednom DbContextu / jednom SaveChanges-u (atomično) — koristi se pri kreiranju zaposlenika zajedno s loginom.</summary>
    Task AddWithLogin(User user, Employee employee);

    /// <summary>`employee` NE SMIJE imati popunjene Locations/Services navigacijske kolekcije (koristiti GetByIdLight).</summary>
    Task Update(Employee employee, List<EmployeeLocation> newLocations, List<EmployeeServiceAssignment> newServices);

    Task SetActiveAndStamp(Guid employeeId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy);
    Task Delete(Employee employee);

    Task<bool> IsUserAlreadyLinked(Guid organizationId, Guid userId, Guid? excludeEmployeeId);
    Task<int> CountActiveAdmins(Guid organizationId);

    /// <summary>Razrješava Employee zapis za trenutno prijavljenog korisnika (npr. za "moji klijenti prvo" sortiranje). Bare row, bez includes.</summary>
    Task<Employee> GetByUserId(Guid organizationId, Guid userId);

    /// <summary>Projekcija izravno na EmployeeDirectoryDto — nikad ne materijalizira osjetljiva polja.</summary>
    Task<(List<EmployeeDirectoryDto> Items, int TotalCount)> GetDirectoryPaged(Guid organizationId, PagedRequest request);
}
