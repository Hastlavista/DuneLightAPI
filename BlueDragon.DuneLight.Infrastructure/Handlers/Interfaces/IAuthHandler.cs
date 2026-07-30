using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAuthHandler
{
    Task<bool> SlugExists(string slug);
    Task AddOrganization(Organization organization);
    Task<Organization> GetOrganizationBySlug(string slug);

    Task AddUser(User user);
    Task<bool> EmailExists(Guid organizationId, string email);
    Task<User> GetUserByCredentials(Guid organizationId, string email, string passwordHash);
    Task<User> GetUserByApiKey(string apiKey);
    Task<User> GetUserById(Guid userId);
    Task UpdatePasswordHash(Guid userId, string passwordHash);

    /// <summary>Koristi modul Zaposlenici pri promjeni uloge zaposlenika.</summary>
    Task UpdateRole(Guid userId, UserRole role);

    /// <summary>Koristi modul Zaposlenici — deaktivacija zaposlenika onemogućuje login povezanog računa.</summary>
    Task SetActive(Guid userId, bool isActive);
}
