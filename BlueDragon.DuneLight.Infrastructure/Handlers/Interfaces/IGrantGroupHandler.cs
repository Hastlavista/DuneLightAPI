using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGrantGroupHandler
{
    Task<List<GrantGroup>> GetAll(Guid organizationId);

    /// <summary>Kreira default GrantGroup-e (Admin/Trener/Recepcija, vidi DefaultGrantGroups) za organizaciju,
    /// unutar iste transakcije kao AuthService.Register. Idempotentno po Name — preskače predloške čija grupa
    /// (po DisplayName) već postoji, ne dira/ne sinkronizira postojeće grupe (vidi FAZA 1 Part D — postojeće
    /// organizacije se namjerno ne mijenjaju).</summary>
    Task EnsureDefaultGrantGroups(IUnitOfWork uow, Guid organizationId);

    /// <summary>Dijagnostika-only, presijeca sve organizacije (namjerno bez organizationId filtera) — koristi ga
    /// SAMO IGrantDiagnosticsService za otkrivanje default-role drifta kod postojećih organizacija. NIKAD ne
    /// koristiti u tenant-facing kodu (vidi FAZA 1 Part H — diagnostika je platform-only).</summary>
    Task<List<GrantGroup>> GetAllAcrossOrganizationsForDiagnostics();
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

    /// <summary>Za RequireGrant provjeru po zahtjevu — vraća je li korisnik Owner i uniju grantova svih njegovih grupa.</summary>
    Task<(bool IsOwner, HashSet<string> Grants)> ResolveEffective(Guid organizationId, Guid userId);

    /// <summary>Bulk lookup GrantGroup naziva po userId — jedan upit za cijelu stranicu/listu korisnika,
    /// izbjegava N+1 (vidi EmployeeService.GetPaged/GetById). Korisnik bez ijedne dodjele izostaje iz rezultata.</summary>
    Task<Dictionary<Guid, List<string>>> GetGrantGroupNamesByUserIds(Guid organizationId, List<Guid> userIds);
}