using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGrantGroupHandler
{
    Task<List<GrantGroup>> GetAll(Guid organizationId);
    Task<GrantGroup> GetById(Guid organizationId, Guid id);
    Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId);
    Task Add(GrantGroup grantGroup);

    /// <summary>Seed-a tri default GrantGroup-e (Admin/Trener/Recepcija, vidi DefaultGrantGroups) za novu organizaciju,
    /// unutar iste transakcije kao AuthService.Register (organizacija + prvi admin user + default grupe = jedna cjelina).</summary>
    Task SeedDefaultGroups(IUnitOfWork uow, Guid organizationId);

    /// <summary>Reconcile grant-key retke (dodaj nove, ukloni izostavljene) unutar jednog SaveChanges-a.</summary>
    Task Update(GrantGroup grantGroup, List<string> newGrantKeys);

    Task<bool> HasAssignedUsers(Guid organizationId, Guid id);
    Task Delete(Guid organizationId, Guid id);

    Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId);

    /// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika.</summary>
    Task SetUserGrantGroups(Guid organizationId, Guid userId, List<Guid> grantGroupIds);

    /// <summary>Za RequireGrant provjeru po zahtjevu — vraća je li korisnik Owner i uniju grantova svih njegovih grupa.</summary>
    Task<(bool IsOwner, HashSet<string> Grants)> ResolveEffective(Guid organizationId, Guid userId);

    /// <summary>Bulk lookup GrantGroup naziva po userId — jedan upit za cijelu stranicu/listu korisnika,
    /// izbjegava N+1 (vidi EmployeeService.GetPaged/GetById). Korisnik bez ijedne dodjele izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, List<string>>> GetGrantGroupNamesByUserIds(Guid organizationId, List<Guid> userIds);
}