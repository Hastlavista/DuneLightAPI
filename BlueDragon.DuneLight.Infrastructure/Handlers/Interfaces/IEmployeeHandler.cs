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
        Guid organizationId, PagedRequest request, Guid? companyId, Guid? engagementTypeId, UserRole? role);

    /// <summary>Puni graf (EngagementType, User, Companies, Services) — za prikaz/čitanje.</summary>
    Task<Employee> GetById(Guid organizationId, Guid id);

    /// <summary>Samo osnovni redak, bez navigacijskih kolekcija — za pripremu mutacije (izbjegava EF tracking sudar s Update/RemoveRange).</summary>
    Task<Employee> GetByIdLight(Guid organizationId, Guid id);

    Task Add(Employee employee);

    /// <summary>Stvara User i Employee u jednom DbContextu / jednom SaveChanges-u (atomično) — koristi se pri kreiranju zaposlenika zajedno s loginom.</summary>
    Task AddWithLogin(User user, Employee employee);

    /// <summary>`employee` NE SMIJE imati popunjene Companies/Services navigacijske kolekcije (koristiti GetByIdLight).</summary>
    Task Update(Employee employee, List<EmployeeCompany> newCompanies, List<EmployeeServiceAssignment> newServices);

    /// <summary>Postavlja Employee.IsActive i povezani User.IsActive u jednom DbContextu / jednom SaveChanges-u (atomično) — sprječava da deaktivirani zaposlenik ostane s aktivnim loginom.</summary>
    Task SetActiveWithLogin(Guid organizationId, Guid employeeId, Guid userId, bool isActive, DateTimeOffset updatedAt, Guid? updatedBy);

    /// <summary>Briše Employee i deaktivira povezani login (User.IsActive = false) u jednom DbContextu / jednom SaveChanges-u (atomično).</summary>
    Task DeleteWithLoginDeactivation(Employee employee);

    Task<bool> IsUserAlreadyLinked(Guid organizationId, Guid userId, Guid? excludeEmployeeId);
    Task<int> CountActiveAdmins(Guid organizationId);

    /// <summary>Razrješava Employee zapis za trenutno prijavljenog korisnika (npr. za "moji klijenti prvo" sortiranje). Bare row, bez includes.</summary>
    Task<Employee> GetByUserId(Guid organizationId, Guid userId);

    /// <summary>Projekcija izravno na EmployeeDirectoryDto — nikad ne materijalizira osjetljiva polja.</summary>
    Task<(List<EmployeeDirectoryDto> Items, int TotalCount)> GetDirectoryPaged(Guid organizationId, PagedRequest request);

    /// <summary>Svi aktivni zaposlenici poslovnice u jednom upitu (Services uključen radi filtera po usluzi) — za available-slots, izbjegava upit po zaposleniku u petlji.</summary>
    Task<List<Employee>> GetForCompany(Guid organizationId, Guid companyId);

    /// <summary>Je li zaposlenik prijavljenog korisnika dodijeljen zadanoj poslovnici — za GET ovlasti gdje zaposlenik smije vidjeti podatke vlastite poslovnice bez posebnog granta (vidi RequireGrantOrAssignedCompanyAttribute).</summary>
    Task<bool> IsUserAssignedToCompany(Guid organizationId, Guid userId, Guid companyId);

    /// <summary>Eksplicitna EmployeeCompany veza (radno mjesto) — bez "prazno = svugdje" fallbacka.
    /// Građevni blok za buduću Appointment eligibility (vidi domensku napomenu na EmployeeCompany).</summary>
    Task<bool> IsEmployeeAssignedToCompany(Guid organizationId, Guid employeeId, Guid companyId);

    /// <summary>Eksplicitna EmployeeServiceAssignment veza (capability) — bez "prazno = sve" fallbacka.
    /// Građevni blok za buduću Appointment eligibility (vidi domensku napomenu na EmployeeServiceAssignment).</summary>
    Task<bool> CanEmployeePerformService(Guid organizationId, Guid employeeId, Guid serviceId);

    /// <summary>Ima li zaposlenik ikakvu povijesnu/poslovnu referencu (termini, pauze, roster, radno vrijeme,
    /// fond godišnjeg, matični trener klijenta/grupe) koja mora blokirati tvrdo brisanje — vidi EmployeeService.Delete.
    /// Namjerno eksplicitno nabrojano po tablici (ne generička refleksija) da se izbjegne oslanjanje na FK grešku iz baze.</summary>
    Task<bool> HasBusinessReferences(Guid organizationId, Guid employeeId);
}
