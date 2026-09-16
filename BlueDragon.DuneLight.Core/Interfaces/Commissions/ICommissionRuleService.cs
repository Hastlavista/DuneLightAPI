using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;

namespace BlueDragon.DuneLight.Core.Interfaces.Commissions;

/// <summary>CRUD nad konfiguracijom provizije (CommissionRule) — vidi CommissionRule.cs.</summary>
public interface ICommissionRuleService
{
    Task<List<CommissionRuleDto>> GetList(Guid organizationId, CommissionRuleQuery query);

    Task<CommissionRuleDto> GetById(Guid organizationId, Guid id);

    Task<CommissionRuleDto> Create(Guid organizationId, Guid userId, CommissionRuleCreateRequest request);

    /// <summary>Mijenja SAMO CalculationType/Value na postojećem retku — sigurno jer CommissionEntry već
    /// snapshotta stare vrijednosti (vidi CommissionRule.cs).</summary>
    Task<CommissionRuleDto> Update(Guid organizationId, Guid userId, Guid id, CommissionRuleUpdateRequest request);

    Task<CommissionRuleDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive);

    /// <summary>Trajno brisanje — odbija se ako pravilo ima ikakvu CommissionEntry povijest (vidi
    /// CommissionRuleHandler.IsReferenced), isto ponašanje kao Service/Product/Package/Employee.</summary>
    Task Delete(Guid organizationId, Guid id);
}
