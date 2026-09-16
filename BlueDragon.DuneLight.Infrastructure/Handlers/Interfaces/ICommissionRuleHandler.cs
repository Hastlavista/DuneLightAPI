using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICommissionRuleHandler
{
    Task<List<CommissionRule>> GetList(Guid organizationId, Guid? employeeId, Core.Enums.CommissionSubjectType? subjectType, bool? isActive);

    Task<CommissionRule> GetById(Guid organizationId, Guid id);

    /// <summary>Aktivno pravilo za točno jedan predmet (isključivo jedan od serviceId/productId/packageId
    /// popunjen) — izvor istine za CommissionLedgerService rezoluciju u trenutku zarade.</summary>
    Task<CommissionRule> GetActiveForSubject(
        Guid organizationId, Guid employeeId, Core.Enums.CommissionSubjectType subjectType,
        Guid? serviceId, Guid? productId, Guid? packageId);

    /// <summary>Sva AKTIVNA pravila jednog Employeea, u jednom upitu — koristi CommissionService.
    /// GenerateForCheckoutCompletion da razriješi proviziju za SVE Product/Package stavke Checkouta jednim
    /// upitom umjesto jednog upita po stavci (vidi spec section 15/61 N+1 upozorenje). Employee je uvijek
    /// niskog broja pravila (jedno po predmetu), pa je "sva aktivna" bounded i jeftino.</summary>
    Task<List<CommissionRule>> GetAllActiveForEmployee(Guid organizationId, Guid employeeId);

    Task Add(CommissionRule rule);

    Task Update(CommissionRule rule);

    Task Delete(CommissionRule rule);

    /// <summary>Ima li pravilo ikakvu CommissionEntry povijest — blokira trajno brisanje (isto ponašanje kao
    /// Service/Product/Package/Employee IsReferenced, vidi spec section 32).</summary>
    Task<bool> IsReferenced(Guid organizationId, Guid id);

    /// <summary>Postoji li AKTIVNO Percentage pravilo za ovu uslugu (bilo kojeg Employeea) — koristi
    /// ServiceCatalogService.EnsureExecutionModeChangeAllowed da spriječi promjenu Service.ExecutionMode u Group
    /// dok takvo pravilo postoji (nema nedvosmislene per-occurrence osnovice za postotak na grupni termin, vidi
    /// spec section 5/16/54 — isti invarijant kao CommissionRuleService.ValidateSubjectAndPercentageRule).</summary>
    Task<bool> HasActivePercentageRuleForService(Guid organizationId, Guid serviceId);
}
