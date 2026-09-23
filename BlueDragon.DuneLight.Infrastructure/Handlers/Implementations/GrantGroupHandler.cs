using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Permissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class GrantGroupHandler : IGrantGroupHandler
{
    private readonly DatabaseSettings _databaseSettings;
    private readonly IDefaultRoleTemplateHandler _defaultRoleTemplateHandler;
    private readonly ICapabilityMaterializationService _capabilityMaterializationService;

    public GrantGroupHandler(DatabaseSettings databaseSettings, IDefaultRoleTemplateHandler defaultRoleTemplateHandler, ICapabilityMaterializationService capabilityMaterializationService)
    {
        _databaseSettings = databaseSettings;
        _defaultRoleTemplateHandler = defaultRoleTemplateHandler;
        _capabilityMaterializationService = capabilityMaterializationService;
    }

    public async Task<List<GrantGroup>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            // AssignedUserCount mora odražavati SAMO trenutno aktivne korisnike (vidi GrantGroupDto.AssignedUserCount
            // napomenu) — filtrirani Include ovdje je namjerno umjesto učitavanja svih UserGrantGroup redaka, jer
            // deaktivacija zaposlenika NE briše UserGrantGroup redak (povijest dodjele se čuva), samo gasi User.IsActive.
            .Include(g => g.UserGrantGroups.Where(ugg => ugg.User.IsActive))
            .Where(g => g.OrganizationId == organizationId)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<GrantGroup> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            // vidi napomenu u GetAll o filtriranom Include-u za AssignedUserCount
            .Include(g => g.UserGrantGroups.Where(ugg => ugg.User.IsActive))
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
    }

    /// <summary>
    /// Grant-only Tenant Authorization Refactor — nove organizacije dobivaju ISKLJUČIVO Admin starter GrantGroup
    /// (materijaliziran preko najnovije aktivne "admin" DefaultRoleTemplate verzije, koja od ove faze uključuje i
    /// permissions.view/manage/assignments.manage). Trener/Recepcija se VIŠE NE kreiraju automatski pri
    /// registraciji — to su bili razvojni/demo starter podaci, ne dio proizvoda (organizacija sama gradi svoju
    /// strukturu preko role-editora nakon registracije). Postojeće organizacije koje već imaju Trener/Recepcija
    /// grupe NISU dirane ovom promjenom (ova metoda se poziva SAMO pri Register, nikad naknadno za postojeći
    /// tenant). Vraća Id Admin grupe (nova ili već postojeća — idempotentno po Name, isto kao prije) da
    /// AuthService.Register može odmah dodijeliti organizacijskog osnivača na nju.
    /// </summary>
    public async Task<Guid?> EnsureDefaultGrantGroups(IUnitOfWork uow, Guid organizationId)
    {
        ResolvedDefaultRoleTemplate template = await _defaultRoleTemplateHandler.GetLatestActiveByKey("admin");
        if (template == null)
            return null;

        GrantGroup existing = await uow.Context.GrantGroups
            .FirstOrDefaultAsync(g => g.OrganizationId == organizationId && g.Name == template.DisplayNameHr);
        if (existing != null)
            return existing.Id;

        GrantGroup group = new GrantGroup
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = template.DisplayNameHr,
            CreatedAt = DateTimeOffset.UtcNow
        };

        uow.Context.GrantGroups.Add(group);
        // Grupa mora postojati u bazi PRIJE ApplyTemplate upiše djecu preko sirovog FK scalara (ne preko
        // navigacije) — isto ponašanje unutar iste otvorene transakcije (vidi IUnitOfWork), ne zaseban commit.
        await uow.Context.SaveChangesAsync();

        await ApplyTemplate(uow, group.Id.GetValueOrDefault(), template, appliedBy: null);

        return group.Id;
    }

    public async Task ApplyTemplate(IUnitOfWork uow, Guid grantGroupId, ResolvedDefaultRoleTemplate template, Guid? appliedBy)
    {
        DatabaseContext context = uow.Context;

        // KRITIČNO (vidi FAZA 1 hardening pass) — ManualAdvancedSet MORA biti izveden iz STARE (trenutne)
        // provenance PRIJE nego što se primijeni novi odabir, ne iz NOVOG predloška. Ako bi se ManualAdvancedSet
        // računao kao existingKeys minus NOVI capability/template skup, grant koji je v2 predložak NAMJERNO uklonio
        // bio bi pogrešno protumačen kao "ručno dodan" i zauvijek preživio — vidi primjer u zadatku (v1={A,B},
        // v2={A}, bez ovoga bi B nepravedno ostao).
        List<GrantGroupCapabilitySnapshot> oldSnapshots = await context.GrantGroupCapabilitySnapshots
            .Include(s => s.CapabilityDefinition).ThenInclude(c => c.Grants)
            .Where(s => s.GrantGroupId == grantGroupId)
            .ToListAsync();

        List<GrantGroupTemplateGrant> oldProvenance = await context.GrantGroupTemplateGrants
            .Where(g => g.GrantGroupId == grantGroupId)
            .ToListAsync();

        HashSet<string> oldCapabilityDerivedSet = new();
        foreach (GrantGroupCapabilitySnapshot snapshot in oldSnapshots)
        {
            List<CapabilityGrantRoleEntry> grantEntries = snapshot.CapabilityDefinition.Grants
                .Select(g => new CapabilityGrantRoleEntry(g.GrantKey, g.Role))
                .ToList();
            oldCapabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(snapshot.CapabilityDefinition.ScopeModel, snapshot.SelectedScope, grantEntries));
        }

        HashSet<string> oldTemplateCompatibilitySet = oldProvenance.Select(g => g.GrantKey).ToHashSet();

        List<GrantGroupGrant> existingGrants = await context.GrantGroupGrants
            .Where(g => g.GrantGroupId == grantGroupId)
            .ToListAsync();
        HashSet<string> existingKeys = existingGrants.Select(g => g.GrantKey).ToHashSet();

        // ManualAdvancedSet — raw grantovi koje NIJEDAN STARI capability/template izvor ne opravdava, dakle ih je
        // Owner ranije ručno dodao preko Advanced editora. Ne dira se OVDJE što je NOVI predložak odabrao.
        HashSet<string> manualAdvancedSet = new(existingKeys);
        manualAdvancedSet.ExceptWith(oldCapabilityDerivedSet);
        manualAdvancedSet.ExceptWith(oldTemplateCompatibilitySet);

        await ApplyResolvedSelections(
            uow,
            grantGroupId,
            template.Selections,
            template.CompatibilityGrants.Select(g => g.GrantKey).ToHashSet(),
            template.TemplateKey,
            template.TemplateVersion,
            manualAdvancedSet,
            appliedBy);
    }

    /// <summary>FAZA 3 (v2 template-upgrade) — dijeljena "zamijeni snapshots/provenance/raw-grants" jezgra koju
    /// koriste i ApplyTemplate (EnsureDefaultGrantGroups za nove organizacije) i novi upgrade-apply put
    /// (GrantGroupTemplateUpgradeService.Apply, nakon što je TemplateUpgradePlanner već izračunao i validirao
    /// razrješene selekcije). Izvučeno iz starog ApplyTemplate tijela BEZ promjene ponašanja — pozivatelj je
    /// odgovoran za izračun manualAdvancedSet iz STARE provenance (vidi ApplyTemplate napomenu zašto to mora biti
    /// prije, ne poslije). FinalGrantSet = materialize(<paramref name="resolvedSelections"/>) UNION
    /// <paramref name="resultingTemplateCompatibilityGrantKeys"/> UNION <paramref name="manualAdvancedSet"/>;
    /// zamjenjuje SVE GrantGroupCapabilitySnapshot i GrantGroupTemplateGrant retke novima, sve unutar
    /// uow.Context, jedan SaveChangesAsync.</summary>
    public async Task ApplyResolvedSelections(
        IUnitOfWork uow,
        Guid grantGroupId,
        IReadOnlyList<TemplateCapabilitySelection> resolvedSelections,
        HashSet<string> resultingTemplateCompatibilityGrantKeys,
        string targetTemplateKey,
        int targetTemplateVersion,
        HashSet<string> manualAdvancedSet,
        Guid? appliedBy)
    {
        DatabaseContext context = uow.Context;

        List<GrantGroupCapabilitySnapshot> oldSnapshots = await context.GrantGroupCapabilitySnapshots
            .Where(s => s.GrantGroupId == grantGroupId)
            .ToListAsync();

        List<GrantGroupTemplateGrant> oldProvenance = await context.GrantGroupTemplateGrants
            .Where(g => g.GrantGroupId == grantGroupId)
            .ToListAsync();

        List<GrantGroupGrant> existingGrants = await context.GrantGroupGrants
            .Where(g => g.GrantGroupId == grantGroupId)
            .ToListAsync();
        HashSet<string> existingKeys = existingGrants.Select(g => g.GrantKey).ToHashSet();

        HashSet<string> newCapabilityDerivedSet = new();
        foreach (TemplateCapabilitySelection selection in resolvedSelections)
            newCapabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));

        HashSet<string> finalGrantSet = new(newCapabilityDerivedSet);
        finalGrantSet.UnionWith(resultingTemplateCompatibilityGrantKeys);
        finalGrantSet.UnionWith(manualAdvancedSet);

        foreach (string key in finalGrantSet.Except(existingKeys))
            context.GrantGroupGrants.Add(new GrantGroupGrant { GrantGroupId = grantGroupId, GrantKey = key });

        foreach (GrantGroupGrant grant in existingGrants)
            if (!finalGrantSet.Contains(grant.GrantKey))
                context.GrantGroupGrants.Remove(grant);

        context.GrantGroupCapabilitySnapshots.RemoveRange(oldSnapshots);
        context.GrantGroupTemplateGrants.RemoveRange(oldProvenance);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (TemplateCapabilitySelection selection in resolvedSelections)
        {
            if (selection.SelectedScope == CapabilitySelectedScope.None)
                continue;

            context.GrantGroupCapabilitySnapshots.Add(new GrantGroupCapabilitySnapshot
            {
                GrantGroupId = grantGroupId,
                CapabilityDefinitionId = selection.CapabilityDefinitionId,
                SelectedScope = selection.SelectedScope,
                SourceTemplateKey = targetTemplateKey,
                SourceTemplateVersion = targetTemplateVersion,
                AppliedAt = now,
                AppliedBy = appliedBy
            });
        }

        foreach (string compatGrantKey in resultingTemplateCompatibilityGrantKeys)
        {
            context.GrantGroupTemplateGrants.Add(new GrantGroupTemplateGrant
            {
                GrantGroupId = grantGroupId,
                GrantKey = compatGrantKey,
                SourceTemplateKey = targetTemplateKey,
                SourceTemplateVersion = targetTemplateVersion,
                AppliedAt = now,
                AppliedBy = appliedBy
            });
        }

        await context.SaveChangesAsync();
    }

    public async Task Add(IUnitOfWork uow, GrantGroup grantGroup)
    {
        uow.Context.GrantGroups.Add(grantGroup);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdateMetadata(IUnitOfWork uow, Guid grantGroupId, string name, Guid updatedBy)
    {
        GrantGroup tracked = await uow.Context.GrantGroups.SingleAsync(g => g.Id == grantGroupId);
        tracked.Name = name;
        tracked.UpdatedAt = DateTimeOffset.UtcNow;
        tracked.UpdatedBy = updatedBy;
    }

    public async Task ApplyCapabilitySelections(IUnitOfWork uow, Guid grantGroupId, IReadOnlyList<TemplateCapabilitySelection> selections, HashSet<string> manualGrantKeys, Guid? appliedBy)
    {
        DatabaseContext context = uow.Context;

        List<GrantGroupCapabilitySnapshot> oldSnapshots = await context.GrantGroupCapabilitySnapshots
            .Where(s => s.GrantGroupId == grantGroupId)
            .ToListAsync();

        // Template compatibility provenance je NAMJERNO netaknuta ovdje — ordinary capability-aware editor save
        // nema mehanizam za mijenjanje template-vlasničkih compatibility grantova (vidi FAZA 2 Part F i
        // ApplyCapabilitySelections klasnu napomenu na sučelju). Čita se svježe unutar iste transakcije da uđe u
        // FinalGrantSet.
        HashSet<string> templateCompatibilitySet = (await context.GrantGroupTemplateGrants
                .Where(g => g.GrantGroupId == grantGroupId)
                .Select(g => g.GrantKey)
                .ToListAsync())
            .ToHashSet();

        List<GrantGroupGrant> existingGrants = await context.GrantGroupGrants
            .Where(g => g.GrantGroupId == grantGroupId)
            .ToListAsync();
        HashSet<string> existingKeys = existingGrants.Select(g => g.GrantKey).ToHashSet();

        HashSet<string> newCapabilityDerivedSet = new();
        foreach (TemplateCapabilitySelection selection in selections)
            newCapabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));

        HashSet<string> finalGrantSet = new(newCapabilityDerivedSet);
        finalGrantSet.UnionWith(templateCompatibilitySet);
        finalGrantSet.UnionWith(manualGrantKeys);

        foreach (string key in finalGrantSet.Except(existingKeys))
            context.GrantGroupGrants.Add(new GrantGroupGrant { GrantGroupId = grantGroupId, GrantKey = key });

        foreach (GrantGroupGrant grant in existingGrants)
            if (!finalGrantSet.Contains(grant.GrantKey))
                context.GrantGroupGrants.Remove(grant);

        context.GrantGroupCapabilitySnapshots.RemoveRange(oldSnapshots);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (TemplateCapabilitySelection selection in selections)
        {
            if (selection.SelectedScope == CapabilitySelectedScope.None)
                continue;

            // Zadrži SourceTemplateKey/Version SAMO ako je ISTA capability-verzija na ISTOM opsegu već postojala —
            // inače je ovo nova/promijenjena selekcija, dakle bez template provenance na razini te capability (vidi
            // FAZA 2 Part F, per-capability provenance umjesto all-or-nothing grupnog flaga).
            GrantGroupCapabilitySnapshot matchingOld = oldSnapshots.SingleOrDefault(s =>
                s.CapabilityDefinitionId == selection.CapabilityDefinitionId && s.SelectedScope == selection.SelectedScope);

            context.GrantGroupCapabilitySnapshots.Add(new GrantGroupCapabilitySnapshot
            {
                GrantGroupId = grantGroupId,
                CapabilityDefinitionId = selection.CapabilityDefinitionId,
                SelectedScope = selection.SelectedScope,
                SourceTemplateKey = matchingOld?.SourceTemplateKey,
                SourceTemplateVersion = matchingOld?.SourceTemplateVersion,
                AppliedAt = now,
                AppliedBy = appliedBy
            });
        }

        await context.SaveChangesAsync();
    }

    public async Task<List<GrantGroupCapabilitySnapshot>> GetCapabilitySnapshots(Guid organizationId, Guid grantGroupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroupCapabilitySnapshots
            // ThenInclude(Grants) — potrebno i za FAZA 2 authoring-state DerivedGrantKeys materijalizaciju, ne samo template-match DTO.
            .Include(s => s.CapabilityDefinition).ThenInclude(c => c.Grants)
            .Where(s => s.GrantGroupId == grantGroupId && s.GrantGroup.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<List<GrantGroupTemplateGrant>> GetTemplateGrantProvenance(Guid organizationId, Guid grantGroupId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroupTemplateGrants
            .Where(g => g.GrantGroupId == grantGroupId && g.GrantGroup.OrganizationId == organizationId)
            .ToListAsync();
    }

    public async Task<List<GrantGroupCapabilitySnapshot>> GetAllCapabilitySnapshotsForDiagnostics()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroupCapabilitySnapshots
            .Include(s => s.CapabilityDefinition).ThenInclude(c => c.Grants)
            .ToListAsync();
    }

    public async Task<List<GrantGroupTemplateGrant>> GetAllTemplateGrantProvenanceForDiagnostics()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroupTemplateGrants.ToListAsync();
    }

    public async Task<List<GrantGroup>> GetAllAcrossOrganizationsForDiagnostics()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            .OrderBy(g => g.OrganizationId).ThenBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<bool> NameExists(Guid organizationId, string name, Guid? excludeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups.AnyAsync(g =>
            g.OrganizationId == organizationId && g.Name == name && (excludeId == null || g.Id != excludeId));
    }

    public async Task Add(GrantGroup grantGroup)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.GrantGroups.Add(grantGroup);
        await context.SaveChangesAsync();
    }

    public async Task Update(GrantGroup grantGroup, List<string> newGrantKeys)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        GrantGroup tracked = await context.GrantGroups.SingleAsync(g => g.Id == grantGroup.Id && g.OrganizationId == grantGroup.OrganizationId);
        tracked.Name = grantGroup.Name;
        tracked.UpdatedAt = grantGroup.UpdatedAt;
        tracked.UpdatedBy = grantGroup.UpdatedBy;

        List<GrantGroupGrant> existing = await context.GrantGroupGrants
            .Where(g => g.GrantGroupId == grantGroup.Id)
            .ToListAsync();
        HashSet<string> existingKeys = existing.Select(g => g.GrantKey).ToHashSet();
        HashSet<string> newKeys = newGrantKeys.ToHashSet();

        foreach (GrantGroupGrant grant in existing)
            if (!newKeys.Contains(grant.GrantKey))
                context.GrantGroupGrants.Remove(grant);

        foreach (string key in newKeys)
            if (!existingKeys.Contains(key))
                context.GrantGroupGrants.Add(new GrantGroupGrant { GrantGroupId = grantGroup.Id.GetValueOrDefault(), GrantKey = key });

        await context.SaveChangesAsync();
    }

    public async Task<bool> HasAssignedUsers(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.UserGrantGroups.AnyAsync(u => u.GrantGroupId == id && u.GrantGroup.OrganizationId == organizationId);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        GrantGroup grantGroup = await context.GrantGroups.SingleOrDefaultAsync(g => g.Id == id && g.OrganizationId == organizationId);
        if (grantGroup == null)
            return;

        context.GrantGroups.Remove(grantGroup);
        await context.SaveChangesAsync();
    }

    public async Task<List<Guid>> GetAssignedGrantGroupIds(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.UserGrantGroups
            .Where(u => u.UserId == userId && u.GrantGroup.OrganizationId == organizationId)
            .Select(u => u.GrantGroupId)
            .ToListAsync();
    }

    public async Task SetUserGrantGroups(Guid organizationId, Guid userId, List<Guid> grantGroupIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        List<UserGrantGroup> existing = await context.UserGrantGroups
            .Where(u => u.UserId == userId && u.GrantGroup.OrganizationId == organizationId)
            .ToListAsync();
        HashSet<Guid> existingIds = existing.Select(u => u.GrantGroupId).ToHashSet();
        HashSet<Guid> newIds = grantGroupIds.ToHashSet();

        foreach (UserGrantGroup userGrantGroup in existing)
            if (!newIds.Contains(userGrantGroup.GrantGroupId))
                context.UserGrantGroups.Remove(userGrantGroup);

        foreach (Guid grantGroupId in newIds)
            if (!existingIds.Contains(grantGroupId))
                context.UserGrantGroups.Add(new UserGrantGroup { UserId = userId, GrantGroupId = grantGroupId });

        await context.SaveChangesAsync();
    }

    public async Task<HashSet<string>> ResolveEffective(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        // Grant-only Tenant Authorization Refactor — nema Owner bypass-a. Organizacijski osnivač dobiva pun
        // pristup isključivo kroz dodjelu Admin starter GrantGroup-e pri registraciji (vidi AuthService.Register),
        // pa se ovdje uvijek računa isključivo iz UserGrantGroup -> GrantGroup -> GrantGroupGrant, bez posebnog
        // slučaja za bilo kojeg korisnika.
        List<string> keys = await context.UserGrantGroups
            .Where(ugg => ugg.UserId == userId && ugg.GrantGroup.OrganizationId == organizationId)
            .SelectMany(ugg => ugg.GrantGroup.Grants.Select(g => g.GrantKey))
            .Distinct()
            .ToListAsync();

        return keys.ToHashSet();
    }

    public async Task<Dictionary<Guid, List<string>>> GetGrantGroupNamesByUserIds(Guid organizationId, List<Guid> userIds)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        var rows = await context.UserGrantGroups
            .Where(ugg => userIds.Contains(ugg.UserId) && ugg.GrantGroup.OrganizationId == organizationId)
            .Select(ugg => new { ugg.UserId, ugg.GrantGroup.Name })
            .ToListAsync();

        return rows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name).ToList());
    }

    public async Task<bool> HasActiveUserWithGrant(
        Guid organizationId,
        string grantKey,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await HasActiveUserWithGrant(context, organizationId, grantKey, overrideGrantGroupId, overrideGrantGroupGrants, overrideUserId, overrideUserGrantGroupIds);
    }

    public async Task<bool> HasActiveUserWithGrantInTransaction(IUnitOfWork uow, Guid organizationId, string grantKey)
    {
        return await HasActiveUserWithGrant(uow.Context, organizationId, grantKey, null, null, null, null);
    }

    private static async Task<bool> HasActiveUserWithGrant(
        DatabaseContext context,
        Guid organizationId,
        string grantKey,
        Guid? overrideGrantGroupId,
        HashSet<string> overrideGrantGroupGrants,
        Guid? overrideUserId,
        List<Guid> overrideUserGrantGroupIds)
    {
        List<Guid> activeUserIds = await context.Users
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .Select(u => u.Id.GetValueOrDefault())
            .ToListAsync();

        var assignmentRows = await context.UserGrantGroups
            .Where(ugg => ugg.GrantGroup.OrganizationId == organizationId)
            .Select(ugg => new { ugg.UserId, ugg.GrantGroupId })
            .ToListAsync();
        List<(Guid UserId, Guid GrantGroupId)> assignments = assignmentRows.Select(r => (r.UserId, r.GrantGroupId)).ToList();

        if (overrideUserId.HasValue)
        {
            assignments = assignments.Where(a => a.UserId != overrideUserId.Value).ToList();
            assignments.AddRange((overrideUserGrantGroupIds ?? new List<Guid>()).Select(gid => (overrideUserId.Value, gid)));
        }

        HashSet<Guid> activeUserIdSet = activeUserIds.ToHashSet();
        List<(Guid UserId, Guid GrantGroupId)> activeAssignments = assignments.Where(a => activeUserIdSet.Contains(a.UserId)).ToList();
        if (activeAssignments.Count == 0)
            return false;

        HashSet<Guid> referencedGroupIds = activeAssignments.Select(a => a.GrantGroupId).ToHashSet();

        var grantGroupGrantRows = await context.GrantGroupGrants
            .Where(g => referencedGroupIds.Contains(g.GrantGroupId))
            .Select(g => new { g.GrantGroupId, g.GrantKey })
            .ToListAsync();
        List<(Guid GrantGroupId, string GrantKey)> grantRows = grantGroupGrantRows.Select(r => (r.GrantGroupId, r.GrantKey)).ToList();

        if (overrideGrantGroupId.HasValue)
        {
            grantRows = grantRows.Where(g => g.GrantGroupId != overrideGrantGroupId.Value).ToList();
            grantRows.AddRange((overrideGrantGroupGrants ?? new HashSet<string>()).Select(key => (overrideGrantGroupId.Value, key)));
        }

        Dictionary<Guid, HashSet<string>> grantsByGroup = grantRows
            .GroupBy(g => g.GrantGroupId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.GrantKey).ToHashSet());

        return activeAssignments.Any(a =>
            grantsByGroup.TryGetValue(a.GrantGroupId, out HashSet<string> grants) && grants.Contains(grantKey));
    }
}