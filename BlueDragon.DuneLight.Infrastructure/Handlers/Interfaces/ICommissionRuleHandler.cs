using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

public interface ICommissionRuleHandler
{
    Task<List<CommissionRule>> GetList(
        Guid organizationId, Guid? employeeId, CommissionRuleKind? kind, CommissionSubjectType? subjectType, bool? isActive);

    Task<CommissionRule> GetById(Guid organizationId, Guid id);

    /// <summary>P2 (2F, Q28.5, Vagaro) — VERZIJA pravila zaposlenika za (vrsta, predmet) izabrana za lokalni datum: najveći
    /// EffectiveFrom &lt;= date UKLJUČUJUĆI deaktivirane verzije (deaktivacija nikad ne vraća stariju verziju; pozivatelj provjerava
    /// CommissionRule.AppliesOn). S razinama. Null = nema verzije. AllServices nema predmeta (subjectId null). Izvor istine za
    /// CommissionService rezoluciju u trenutku zarade.</summary>
    Task<CommissionRule> GetVersionOn(
        Guid organizationId, Guid employeeId, CommissionRuleKind kind, CommissionSubjectType subjectType, Guid? subjectId, DateOnly date);

    Task Add(CommissionRule rule);

    /// <summary>Sprema izmjenu verzije pravila; kad je <paramref name="tiers"/> zadan, zamjenjuje sve razine verzije.</summary>
    Task Update(CommissionRule rule, List<CommissionRuleTier> tiers = null);

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
