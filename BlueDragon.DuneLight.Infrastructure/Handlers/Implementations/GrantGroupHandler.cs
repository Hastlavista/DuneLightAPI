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
            .Include(g => g.UserGrantGroups)
            .Where(g => g.OrganizationId == organizationId)
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<GrantGroup> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.GrantGroups
            .Include(g => g.Grants)
            .Include(g => g.UserGrantGroups)
            .SingleOrDefaultAsync(g => g.OrganizationId == organizationId && g.Id == id);
    }

    /// <summary>FAZA 1 Part P — nove organizacije se od uvođenja capability sustava kreiraju MATERIJALIZACIJOM
    /// DefaultRoleTemplate v1 (admin/trener/recepcija), ne više duplicirane hardkodirane liste u DefaultGrantGroups
    /// (koja i dalje postoji kao oracle za dijagnostiku/migracijski backfill — vidi DefaultGrantGroups klasnu
    /// napomenu). Rezultat mora biti byte-for-byte identičan starom ponašanju — vidi migraciju koja seedа v1
    /// predloške da vjerno reproduciraju DefaultGrantGroups.AdminGrants/TrenerGrants/RecepcijaGrants.</summary>
    public async Task EnsureDefaultGrantGroups(IUnitOfWork uow, Guid organizationId)
    {
        HashSet<string> existingNames = (await uow.Context.GrantGroups
                .Where(g => g.OrganizationId == organizationId)
                .Select(g => g.Name)
                .ToListAsync())
            .ToHashSet();

        foreach (string templateKey in new[] { "admin", "trener", "recepcija" })
        {
            ResolvedDefaultRoleTemplate template = await _defaultRoleTemplateHandler.GetLatestActiveByKey(templateKey);
            if (template == null)
                continue;

            if (existingNames.Contains(template.DisplayNameHr))
                continue;

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
        }
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

        HashSet<string> newCapabilityDerivedSet = new();
        foreach (TemplateCapabilitySelection selection in template.Selections)
            newCapabilityDerivedSet.UnionWith(_capabilityMaterializationService.Materialize(selection.ScopeModel, selection.SelectedScope, selection.Grants));

        HashSet<string> newTemplateCompatibilitySet = template.CompatibilityGrants.Select(g => g.GrantKey).ToHashSet();

        HashSet<string> finalGrantSet = new(newCapabilityDerivedSet);
        finalGrantSet.UnionWith(newTemplateCompatibilitySet);
        finalGrantSet.UnionWith(manualAdvancedSet);

        foreach (string key in finalGrantSet.Except(existingKeys))
            context.GrantGroupGrants.Add(new GrantGroupGrant { GrantGroupId = grantGroupId, GrantKey = key });

        foreach (GrantGroupGrant grant in existingGrants)
            if (!finalGrantSet.Contains(grant.GrantKey))
                context.GrantGroupGrants.Remove(grant);

        context.GrantGroupCapabilitySnapshots.RemoveRange(oldSnapshots);
        context.GrantGroupTemplateGrants.RemoveRange(oldProvenance);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (TemplateCapabilitySelection selection in template.Selections)
        {
            if (selection.SelectedScope == CapabilitySelectedScope.None)
                continue;

            context.GrantGroupCapabilitySnapshots.Add(new GrantGroupCapabilitySnapshot
            {
                GrantGroupId = grantGroupId,
                CapabilityDefinitionId = selection.CapabilityDefinitionId,
                SelectedScope = selection.SelectedScope,
                SourceTemplateKey = template.TemplateKey,
                SourceTemplateVersion = template.TemplateVersion,
                AppliedAt = now,
                AppliedBy = appliedBy
            });
        }

        foreach (TemplateCompatibilityGrant compat in template.CompatibilityGrants)
        {
            context.GrantGroupTemplateGrants.Add(new GrantGroupTemplateGrant
            {
                GrantGroupId = grantGroupId,
                GrantKey = compat.GrantKey,
                SourceTemplateKey = template.TemplateKey,
                SourceTemplateVersion = template.TemplateVersion,
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

    public async Task<(bool IsOwner, HashSet<string> Grants)> ResolveEffective(Guid organizationId, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        User user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId);
        if (user == null)
            return (false, new HashSet<string>());

        if (user.IsOwner)
            return (true, new HashSet<string>());

        List<string> keys = await context.UserGrantGroups
            .Where(ugg => ugg.UserId == userId)
            .SelectMany(ugg => ugg.GrantGroup.Grants.Select(g => g.GrantKey))
            .Distinct()
            .ToListAsync();

        return (false, keys.ToHashSet());
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
}