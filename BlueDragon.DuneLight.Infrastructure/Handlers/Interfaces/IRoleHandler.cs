using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IRoleHandler
{
    Task<List<Role>> GetAll(Guid organizationId);
    Task<Role> GetById(Guid organizationId, Guid id);
    Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId);
    Task Add(Role role);
    Task Update(Role role);
    Task Delete(Guid organizationId, Guid id);

    Task<List<Guid>> GetAssignedRoleIds(Guid organizationId, Guid userId);

    /// <summary>Zamjenjuje CIJELI skup Role dodjela za korisnika.</summary>
    Task SetUserRoles(Guid organizationId, Guid userId, List<Guid> roleIds);

    /// <summary>Bulk lookup Role naziva po userId — jedan upit za cijelu stranicu/listu korisnika,
    /// izbjegava N+1 (vidi EmployeeService.GetPaged/GetById). Korisnik bez ijedne dodjele izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, List<string>>> GetRoleNamesByUserIds(Guid organizationId, List<Guid> userIds);
}