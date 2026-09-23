using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Capabilities;

namespace BlueDragon.DuneLight.Core.Interfaces.Capabilities;

/// <summary>FAZA 3 — orkestrira review/apply tok za predložak-verzija nadogradnju (Admin/Trener/Recepcija v1->v2 i
/// buduće verzije). Backend je jedini autoritativan izvor: publiciranje nove DefaultRoleTemplate verzije NIKAD samo
/// od sebe ne mijenja postojeći GrantGroup — Owner mora eksplicitno pregledati diff i kliknuti Primijeni (vidi
/// TemplateUpgradePlanner za sam algoritam, ovaj servis samo puni ulaz iz baze/mapira DTO-e/upravlja
/// stateToken-om/perzistencijom). Zaštićeno permissions.manage grantom (vidi GrantGroupsController RequireGrant).</summary>
public interface IGrantGroupTemplateUpgradeService
{
    Task<GrantGroupTemplateUpgradeStatusDto> GetUpgradeStatus(Guid organizationId, Guid grantGroupId);

    /// <summary>Čisto read-only — bez upisa u bazu. Resolutions su prazne (planner ih puni pri Preview/Apply).</summary>
    Task<GrantGroupTemplateDiffDto> GetDiff(Guid organizationId, Guid grantGroupId, int targetTemplateVersion);

    /// <summary>Isti izračun kao Apply (isti planner poziv s istim resolutions), ali BEZ upisa u bazu — pokazuje
    /// Owner-u točno što bi Apply napravio prije nego stvarno klikne Primijeni.</summary>
    Task<GrantGroupTemplateUpgradePlanDto> Preview(Guid organizationId, Guid grantGroupId, GrantGroupTemplateUpgradePlanRequest request);

    /// <summary>Jedna atomska transakcija: primjenjuje razriješene selekcije (GrantGroupHandler.ApplyResolvedSelections),
    /// upisuje audit log redak, commit, vraća svježe autoritativno stanje (GrantGroupCapabilityAuthoringService.GetAuthoringState).</summary>
    Task<GrantGroupAuthoringDto> Apply(Guid organizationId, Guid userId, Guid grantGroupId, GrantGroupTemplateUpgradePlanRequest request);
}
