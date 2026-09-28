using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IPlatformAccountHandler
{
    /// <summary>Email is globally unique for PlatformAccount (unlike tenant Users, where it's only unique per
    /// Organization) — see the platform_accounts migration.</summary>
    Task<PlatformAccount> GetByEmail(string email);

    Task<PlatformAccount> GetById(Guid id);

    Task<bool> IsActive(Guid id);

    Task<PlatformAccount> Create(PlatformAccount account);
}
