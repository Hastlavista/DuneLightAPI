using System;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

/// <summary>T1-4 — PRIVREMENI testni alat (seed), uklanja se prije go-livea. Samo čitanja koja seed treba da odabere aktera.</summary>
public interface IDemoSeedHandler
{
    /// <summary>Najstariji aktivni korisnik organizacije dodijeljen grupi s <c>system_key = 'admin'</c>, ili null.</summary>
    Task<Guid?> FindActiveAdminUserId(Guid organizationId);

    /// <summary>Najstarija grupa ovlasti organizacije s <c>system_key = 'admin'</c>, ili null.</summary>
    Task<Guid?> FindSystemAdminGrantGroupId(Guid organizationId);
}
