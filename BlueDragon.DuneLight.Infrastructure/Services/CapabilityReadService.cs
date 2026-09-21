using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;
using BlueDragon.DuneLight.Core.Interfaces.Capabilities;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Capabilities;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi ICapabilityReadService — čisto read-only, FAZA 1 Part R.</summary>
public class CapabilityReadService : ICapabilityReadService
{
    private readonly ICapabilityDefinitionHandler _capabilityDefinitionHandler;
    private readonly IDefaultRoleTemplateHandler _defaultRoleTemplateHandler;
    private readonly IGrantGroupHandler _grantGroupHandler;

    public CapabilityReadService(ICapabilityDefinitionHandler capabilityDefinitionHandler, IDefaultRoleTemplateHandler defaultRoleTemplateHandler, IGrantGroupHandler grantGroupHandler)
    {
        _capabilityDefinitionHandler = capabilityDefinitionHandler;
        _defaultRoleTemplateHandler = defaultRoleTemplateHandler;
        _grantGroupHandler = grantGroupHandler;
    }

    public async Task<List<CapabilityDefinitionDto>> GetLatestActiveDefinitions()
    {
        List<CapabilityDefinition> definitions = await _capabilityDefinitionHandler.GetLatestActive();
        return definitions.Select(ToDto).ToList();
    }

    public async Task<CapabilityDefinitionDto> GetDefinitionDetails(string key, int? version)
    {
        CapabilityDefinition definition = await _capabilityDefinitionHandler.GetByKey(key, version);
        if (definition == null)
            throw new NotFoundAppException("CapabilityDefinition", key);

        return ToDto(definition);
    }

    public async Task<List<DefaultRoleTemplateDto>> GetLatestActiveTemplates()
    {
        List<DefaultRoleTemplate> templates = await _defaultRoleTemplateHandler.GetLatestActive();
        return templates.Select(ToDto).ToList();
    }

    public async Task<DefaultRoleTemplateDto> GetTemplateDetails(string key, int? version)
    {
        DefaultRoleTemplate template = await _defaultRoleTemplateHandler.GetByKey(key, version);
        if (template == null)
            throw new NotFoundAppException("DefaultRoleTemplate", key);

        return ToDto(template);
    }

    public async Task<GrantGroupTemplateMatchDto> GetGrantGroupTemplateMatch(Guid organizationId, Guid grantGroupId)
    {
        List<GrantGroupCapabilitySnapshot> snapshots = await _grantGroupHandler.GetCapabilitySnapshots(organizationId, grantGroupId);
        List<GrantGroupTemplateGrant> templateGrants = await _grantGroupHandler.GetTemplateGrantProvenance(organizationId, grantGroupId);

        string sourceTemplateKey = snapshots.Select(s => s.SourceTemplateKey).FirstOrDefault(k => k != null);
        int? sourceTemplateVersion = snapshots.Select(s => s.SourceTemplateVersion).FirstOrDefault(v => v.HasValue);

        return new GrantGroupTemplateMatchDto(
            grantGroupId,
            HasSnapshotMetadata: snapshots.Count > 0,
            sourceTemplateKey,
            sourceTemplateVersion,
            snapshots.Select(s => new GrantGroupCapabilitySnapshotDto(
                s.CapabilityDefinitionId, s.CapabilityDefinition.Key, s.CapabilityDefinition.Version,
                s.SelectedScope, s.SourceTemplateKey, s.SourceTemplateVersion, s.AppliedAt)).ToList(),
            templateGrants.Select(g => new GrantGroupTemplateGrantDto(g.GrantKey, g.SourceTemplateKey, g.SourceTemplateVersion, g.AppliedAt)).ToList());
    }

    private static CapabilityDefinitionDto ToDto(CapabilityDefinition definition)
    {
        return new CapabilityDefinitionDto(
            definition.Id.GetValueOrDefault(),
            definition.Key,
            definition.Version,
            definition.CategoryKey,
            definition.ScopeModel,
            definition.Sensitivity,
            definition.IsActive,
            definition.DeprecatedAt,
            definition.Grants.Select(g => new CapabilityDefinitionGrantDto(g.GrantKey, g.Role)).OrderBy(g => g.GrantKey).ToList());
    }

    private static DefaultRoleTemplateDto ToDto(DefaultRoleTemplate template)
    {
        return new DefaultRoleTemplateDto(
            template.Id.GetValueOrDefault(),
            template.Key,
            template.Version,
            template.DisplayNameHr,
            template.IsActive,
            template.Capabilities.Select(c => new DefaultRoleTemplateCapabilityDto(
                c.CapabilityDefinitionId, c.CapabilityDefinition.Key, c.CapabilityDefinition.Version, c.SelectedScope)).ToList(),
            template.CompatibilityGrants.Select(g => new DefaultRoleTemplateGrantDto(g.GrantKey, g.Reason)).ToList());
    }
}
