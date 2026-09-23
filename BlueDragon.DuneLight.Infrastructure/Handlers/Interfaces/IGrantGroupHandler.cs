using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface IGrantGroupHandler
{
    Task<List<GrantGroup>> GetAll(Guid organizationId);

    /// <summary>Kreira ISKLJUČIVO Admin starter GrantGroup za novu organizaciju (Grant-only Tenant Authorization
    /// Refactor — Trener/Recepcija više nisu automatski bootstrap), unutar iste transakcije kao
    /// AuthService.Register. Idempotentno po Name. Vraća Id Admin grupe za trenutnu dodjelu osnivača.</summary>
    Task<Guid?> EnsureDefaultGrantGroups(IUnitOfWork uow, Guid organizationId);

    /// <summary>Dijagnostika-only, presijeca sve organizacije (namjerno bez organizationId filtera) — koristi ga
    /// SAMO IGrantDiagnosticsService za otkrivanje default-role drifta kod postojećih organizacija. NIKAD ne
    /// koristiti u tenant-facing kodu (vidi FAZA 1 Part H — diagnostika je platform-only).</summary>
    Task<List<GrantGroup>> GetAllAcrossOrganizationsForDiagnostics();
    Task<GrantGroup> GetById(Guid organizationId, Guid id);
    Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId);
    Task Add(GrantGroup grantGroup);

    /// <summary>Reconcile grant-key retke (dodaj nove, ukloni izostavljene) unutar jednog SaveChanges-a.</summary>
    Task Update(GrantGroup grantGroup, List<string> newGrantKeys);

    /// <summary>FAZA 2 Part D — isto kao Add(GrantGroup), ali unutar postojeće transakcije (vidi
    /// GrantGroupCapabilityAuthoringService.Create — grupa mora postojati u bazi PRIJE
    /// ApplyCapabilitySelections upiše djecu preko sirovog FK scalara, isti obrazac kao EnsureDefaultGrantGroups).</summary>
    Task Add(IUnitOfWork uow, GrantGroup grantGroup);

    /// <summary>FAZA 2 Part E — mijenja SAMO Name/UpdatedAt/UpdatedBy unutar iste transakcije kao
    /// ApplyCapabilitySelections (jedan SaveChangesAsync za cijeli capability-aware save, vidi Part N).</summary>
    Task UpdateMetadata(IUnitOfWork uow, Guid grantGroupId, string name, Guid updatedBy);

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

    /// <summary>FAZA 1 Part J — puni "final grant set" algoritam. ManualAdvancedSet se računa iz STARE (trenutne)
    /// GrantGroupCapabilitySnapshot/GrantGroupTemplateGrant provenance — NIKAD iz novog <paramref name="template"/>
    /// parametra — jer inače grant koji nova verzija namjerno uklanja biva pogrešno protumačen kao "ručno dodan" i
    /// zauvijek preživi (vidi FAZA 1 hardening pass). Zatim materijalizira NOVI predložak preko
    /// ICapabilityMaterializationService, izračuna FinalGrantSet = NewCapabilityDerivedSet UNION
    /// NewTemplateCompatibilitySet UNION ManualAdvancedSet, diff-a s trenutnim GrantGroupGrant retcima (dodaj
    /// nedostajuće, ukloni SAMO one bez preostalog izvora), te zamijeni GrantGroupCapabilitySnapshot i
    /// GrantGroupTemplateGrant provenance retke novima — sve unutar uow.Context, jedan SaveChangesAsync. Radi i za
    /// novu (praznu, bez stare provenance) i za postojeću GrantGroup (koristi ga i EnsureDefaultGrantGroups i
    /// budući role-editor).</summary>
    Task ApplyTemplate(IUnitOfWork uow, Guid grantGroupId, Core.Interfaces.Capabilities.ResolvedDefaultRoleTemplate template, Guid? appliedBy);

    /// <summary>FAZA 3 (v2 template-upgrade) — dijeljena jezgra izvučena iz ApplyTemplate (vidi tamošnju napomenu).
    /// Pozivatelj (ApplyTemplate ILI GrantGroupTemplateUpgradeService.Apply) mora unaprijed izračunati
    /// <paramref name="manualAdvancedSet"/> iz STARE (trenutne) provenance — ova metoda ga NE izvodi sama.
    /// Zamjenjuje SVE GrantGroupCapabilitySnapshot/GrantGroupTemplateGrant retke novima i diff-a GrantGroupGrant
    /// prema FinalGrantSet = materialize(resolvedSelections) UNION resultingTemplateCompatibilityGrantKeys UNION
    /// manualAdvancedSet — sve unutar uow.Context, jedan SaveChangesAsync.</summary>
    Task ApplyResolvedSelections(
        IUnitOfWork uow,
        Guid grantGroupId,
        IReadOnlyList<Core.Interfaces.Capabilities.TemplateCapabilitySelection> resolvedSelections,
        HashSet<string> resultingTemplateCompatibilityGrantKeys,
        string targetTemplateKey,
        int targetTemplateVersion,
        HashSet<string> manualAdvancedSet,
        Guid? appliedBy);

    /// <summary>FAZA 2 Part E/F/G — capability-aware autorstvo za create ILI ordinary edit (razlikuje se od
    /// ApplyTemplate: <paramref name="manualGrantKeys"/> je Ownerov EKSPLICITNI zahtijevani skup iz editora, ne
    /// automatski očuvan stari manual skup, i GrantGroupTemplateGrant provenance retci se NIKAD ne diraju ovdje —
    /// ordinary edit nema mehanizam za mijenjanje template-compatibility grantova, vidi klasnu napomenu na
    /// GrantGroupTemplateGrant i FAZA 2 Part F). FinalGrantSet = materialize(<paramref name="selections"/>) UNION
    /// (trenutni GrantGroupTemplateGrant.GrantKey skup, pročitan svježe unutar iste transakcije) UNION
    /// <paramref name="manualGrantKeys"/>. Zamjenjuje SVE GrantGroupCapabilitySnapshot retke; za svaku selekciju
    /// zadržava SourceTemplateKey/Version SAMO ako je isti CapabilityDefinitionId+SelectedScope postojao u starom
    /// snapshotu (inače null — capability je novo-odabrana/promijenjenog opsega, dakle "customized" na razini te
    /// jedne capability, ne cijele grupe).</summary>
    Task ApplyCapabilitySelections(IUnitOfWork uow, Guid grantGroupId, IReadOnlyList<Core.Interfaces.Capabilities.TemplateCapabilitySelection> selections, HashSet<string> manualGrantKeys, Guid? appliedBy);

    Task<List<GrantGroupCapabilitySnapshot>> GetCapabilitySnapshots(Guid organizationId, Guid grantGroupId);
    Task<List<GrantGroupTemplateGrant>> GetTemplateGrantProvenance(Guid organizationId, Guid grantGroupId);

    /// <summary>Dijagnostika-only, presijeca sve organizacije — vidi GetAllAcrossOrganizationsForDiagnostics.</summary>
    Task<List<GrantGroupCapabilitySnapshot>> GetAllCapabilitySnapshotsForDiagnostics();
    Task<List<GrantGroupTemplateGrant>> GetAllTemplateGrantProvenanceForDiagnostics();

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
    /// Koristi se PRIJE mutacije (Delete/raw Update/SetUserGrantGroups/Employee deaktivacija) — za mutacije koje
    /// idu kroz IUnitOfWork i već pišu novi grant skup unutar transakcije (capability-based Update, template
    /// upgrade Apply), vidi <see cref="HasActiveUserWithGrantInTransaction"/> umjesto ovoga.
    /// </summary>
    Task<bool> HasActiveUserWithGrant(
        Guid organizationId,
        string grantKey,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null);

    /// <summary>Isto kao <see cref="HasActiveUserWithGrant"/>, ali čita iz VEĆ OTVORENE transakcije
    /// (<paramref name="uow"/>.Context) umjesto novog DbContext-a — vidi ApplyCapabilitySelections/
    /// ApplyResolvedSelections koji unutar iste transakcije već upisuju novi GrantGroupGrant skup (uz
    /// SaveChangesAsync, ali PRIJE uow.CommitAsync), pa ovaj poziv ODMAH NAKON njih vidi stvarno novo stanje bez
    /// potrebe za override parametrima.</summary>
    Task<bool> HasActiveUserWithGrantInTransaction(IUnitOfWork uow, Guid organizationId, string grantKey);
}