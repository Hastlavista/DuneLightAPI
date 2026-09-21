using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>FAZA 1 Part R — read-only servis za budući frontend role-editor. Ne mijenja ništa; sve mutacije
/// (odabir capability-ja, nova verzija) ostaju za kasniju fazu.</summary>
public interface ICapabilityReadService
{
    Task<List<CapabilityDefinitionDto>> GetLatestActiveDefinitions();
    Task<CapabilityDefinitionDto> GetDefinitionDetails(string key, int? version);
    Task<List<DefaultRoleTemplateDto>> GetLatestActiveTemplates();
    Task<DefaultRoleTemplateDto> GetTemplateDetails(string key, int? version);
    Task<GrantGroupTemplateMatchDto> GetGrantGroupTemplateMatch(Guid organizationId, Guid grantGroupId);
}
