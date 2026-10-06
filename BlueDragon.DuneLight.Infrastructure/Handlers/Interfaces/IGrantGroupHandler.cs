using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGrantGroupHandler
{
    Task<List<GrantGroup>> GetAll(Guid organizationId);

    /// <summary>Inicijalizacija organizacije (ADR-0023): kreira inicijalnu Admin GrantGroup (system_key
    /// SystemGrantGroups.Admin) sa SVIM grantovima iz Grants.Catalog, unutar transakcije registracije. Vraća Id grupe za
    /// dodjelu prvog korisnika. Nema predloška.</summary>
    Task<Guid> CreateSystemAdminGroup(IUnitOfWork uow, Guid organizationId);

    Task<GrantGroup> GetById(Guid organizationId, Guid id);
    Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId);
    Task Add(GrantGroup grantGroup);

    /// <summary>Reconcile grant-key retke (dodaj nove, ukloni izostavljene) unutar jednog SaveChanges-a.</summary>
    Task Update(GrantGroup grantGroup, List<string> newGrantKeys);

    Task<bool> HasAssignedUsers(Guid organizationId, Guid id);
    Task Delete(Guid organizationId, Guid id);

    Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId);

    /// <summary>Zamjenjuje CIJELI skup GrantGroup dodjela za korisnika.</summary>
    Task SetUserGrantGroups(Guid organizationId, Guid userId, List<Guid> grantGroupIds);

    /// <summary>Za RequireGrant provjeru po zahtjevu — vraća uniju efektivnih raw grantova svih GrantGroup-a
    /// dodijeljenih korisniku. Nema Owner bypass-a (Grant-only Tenant Authorization Refactor).</summary>
    Task<HashSet<string>> ResolveEffective(Guid organizationId, Guid userId);

    /// <summary>Bulk lookup GrantGroup naziva po userId — jedan upit za cijelu stranicu/listu korisnika,
    /// izbjegava N+1 (vidi EmployeeService.GetPaged/GetById). Korisnik bez ijedne dodjele izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, List<string>>> GetGrantGroupNamesByUserIds(Guid organizationId, List<Guid> userIds);

    /// <summary>
    /// Grant-only Tenant Authorization Refactor — last-permission-admin lockout provjera. Vraća true ako BAREM
    /// JEDAN aktivan (User.IsActive) korisnik organizacije efektivno ima <paramref name="grantKey"/>, RAČUNAJUĆI
    /// zamišljenu (još nespremljenu) promjenu opisanu override parametrima, umjesto stvarnog trenutnog stanja iz
    /// baze:
    /// - <paramref name="overrideGrantGroupId"/>/<paramref name="overrideGrantGroupGrants"/>: ta GrantGroup se
    ///   tretira kao da ima TOČNO ovaj grant-skup (prazan skup = grupa je obrisana/nema grantova), umjesto
    ///   stvarnih GrantGroupGrant redaka.
    /// - <paramref name="overrideUserId"/>/<paramref name="overrideUserGrantGroupIds"/>: taj korisnik se tretira
    ///   kao da je dodijeljen TOČNO ovim GrantGroup-ama (prazna lista = bez dodjela, uključujući simulaciju
    ///   deaktivacije — korisnik bez efektivnih grantova jednako ne štiti invarijantu bez obzira je li razlog
    ///   "deaktiviran" ili "bez dodjela"), umjesto stvarnih UserGrantGroup redaka.
    /// Koristi se PRIJE mutacije (Delete/Update/SetUserGrantGroups/Employee deaktivacija; capability editor ide kroz
    /// isti Update put).
    /// </summary>
    Task<bool> HasActiveUserWithGrant(
        Guid organizationId,
        string grantKey,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null);

}