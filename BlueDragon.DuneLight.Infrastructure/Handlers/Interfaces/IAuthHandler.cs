using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IAuthHandler
{
    Task<bool> SlugExists(string slug);
    Task AddOrganization(Organization organization);

    /// <summary>Kao <see cref="AddOrganization(Organization)"/>, ali unutar zajedničke transakcije (organizacija +
    /// prvi admin user kao jedna atomična cjelina, vidi AuthService.Register) — vidi IUnitOfWork.</summary>
    Task AddOrganization(IUnitOfWork uow, Organization organization);

    Task<Organization> GetOrganizationBySlug(string slug);

    Task AddUser(User user);

    /// <summary>Kao <see cref="AddUser(User)"/>, ali unutar zajedničke transakcije — vidi IUnitOfWork.</summary>
    Task AddUser(IUnitOfWork uow, User user);
    Task<bool> EmailExists(Guid organizationId, string email);
    Task<User> GetUserByCredentials(Guid organizationId, string email, string passwordHash);
    Task<User> GetUserByApiKey(string apiKey);
    Task<User> GetUserById(Guid userId);
    Task UpdatePasswordHash(Guid userId, string passwordHash);

    /// <summary>Koristi modul Zaposlenici pri promjeni uloge zaposlenika — organizationId je defense-in-depth
    /// (employee je već org-scoped na pozivnom mjestu, ali upit ovdje ponovno filtrira samostalno).</summary>
    Task UpdateRole(Guid organizationId, Guid userId, UserRole role);
}
