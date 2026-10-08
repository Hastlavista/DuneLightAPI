using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>P2 — mapiranje nepromjenjive verzije plana u DTO i upozorenja nad njom; dijele ga katalog planova i članstva
/// (uvjeti članstva su verzija plana). Zahtijeva učitane Services.Service, Companies.Company i UsageLimits.Service.</summary>
public static class MembershipPlanReadModel
{
    public static MembershipPlanVersionDto ToDto(MembershipPlanVersion version) => new()
    {
        Id = version.Id,
        Version = version.Version,
        Price = version.Price,
        StartFee = version.StartFee,
        BillingInterval = version.BillingInterval,
        RenewalAnchor = version.RenewalAnchor,
        CompanyScope = version.CompanyScope,
        Companies = version.Companies
            .OrderBy(c => c.Company?.Name)
            .Select(c => new MembershipPlanCompanyDto { CompanyId = c.CompanyId, CompanyName = c.Company?.Name, IsActive = c.Company?.IsActive == true })
            .ToList(),
        Services = version.Services
            .OrderBy(s => s.Service?.Name)
            .Select(s => new MembershipPlanServiceDto { ServiceId = s.ServiceId, ServiceName = s.Service?.Name, IsActive = s.Service?.IsActive == true })
            .ToList(),
        UsageLimits = version.UsageLimits
            .OrderBy(l => l.ServiceId.HasValue).ThenBy(l => l.Service?.Name).ThenBy(l => l.Window)
            .Select(l => new MembershipUsageLimitDto { ServiceId = l.ServiceId, ServiceName = l.Service?.Name, Window = l.Window, MaxUses = l.MaxUses })
            .ToList(),
        PriceBenefits = version.PriceBenefits
            .OrderBy(b => b.Scope == MembershipPriceBenefitScope.Service).ThenBy(b => b.Service?.Name)
            .Select(b => new MembershipPriceBenefitDto { Scope = b.Scope, ServiceId = b.ServiceId, ServiceName = b.Service?.Name, Type = b.Type, Value = b.Value })
            .ToList(),
        MinimumCommitmentPeriods = version.MinimumCommitmentPeriods,
        CancellationNoticeDays = version.CancellationNoticeDays,
        Pause = new MembershipPauseRulesDto
        {
            Allowed = version.PauseAllowed,
            MaxPauseDays = version.MaxPauseDays,
            MaxPausePeriods = version.MaxPausePeriods,
            MaxPausesPer12Months = version.MaxPausesPer12Months,
            ExtendsPeriod = version.PauseExtendsPeriod
        },
        CreatedAt = version.CreatedAt,
        CreatedBy = version.CreatedBy
    };

    /// <summary>Q49 limiti bez učinka i Q29.3 (sve odabrane poslovnice neaktivne).</summary>
    public static List<WarningDto> WarningsOf(MembershipPlanVersion version)
    {
        List<WarningDto> warnings = MembershipPlanRules.LimitWarnings(
            version.BillingInterval,
            version.UsageLimits.Select(l => new MembershipLimitSpec(l.ServiceId, l.Window, l.MaxUses)).ToList());

        if (version.CompanyScope == MembershipCompanyScope.SelectedCompanies && version.Companies.All(c => c.Company?.IsActive != true))
            warnings.Add(new WarningDto(WarningCodes.MembershipPlanNoActiveCompany, new WarningMembershipPlanCompaniesDetails
            {
                CompanyIds = version.Companies.Select(c => c.CompanyId).ToList()
            }));

        return warnings;
    }

    public static MembershipPeriodTerms PeriodTerms(MembershipPlanVersion version) =>
        new(version.BillingInterval, version.RenewalAnchor, version.PauseExtendsPeriod);
}
