using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.DTOs.Organization;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Poslovne postavke organizacije (vidi OrganizationSettings.cs). Redak je opcionalan po Organization: nepostojeći redak
/// znači "koristi platformski default". P1 (D1): rok otkazivanja je uklonjen odavde — prozor je dio verzije politike
/// otkazivanja (ICancellationPolicyResolver).
/// </summary>
public class OrganizationSettingsService : IOrganizationSettingsService
{
    private readonly IOrganizationSettingsHandler _handler;

    public OrganizationSettingsService(IOrganizationSettingsHandler handler)
    {
        _handler = handler;
    }

    /// <summary>Phase D3B3A — jedino trenutno podržano ponašanje (vidi PackageConsumptionTiming).</summary>
    public const PackageConsumptionTiming DefaultPackageConsumptionTiming = PackageConsumptionTiming.OnCompletion;

    public async Task<PackageConsumptionTiming> GetPackageConsumptionTiming(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return settings?.PackageConsumptionTiming ?? DefaultPackageConsumptionTiming;
    }

    /// <summary>P2 (Q14) — default roka najave izmjene plana za postojeća članstva kad redak postavki ne postoji.</summary>
    public const int DefaultMembershipChangeNoticeDays = 30;

    public async Task<int> GetMembershipChangeNoticeDays(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return settings?.MembershipChangeNoticeDays ?? DefaultMembershipChangeNoticeDays;
    }

    public async Task<OrganizationSettingsDto> GetSettings(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return new OrganizationSettingsDto
        {
            PackageConsumptionTiming = settings?.PackageConsumptionTiming ?? DefaultPackageConsumptionTiming,
            TimeZone = await _handler.GetTimeZone(organizationId) ?? OrganizationTimeZones.Default,
            MembershipChangeNoticeDays = settings?.MembershipChangeNoticeDays ?? DefaultMembershipChangeNoticeDays,
            MembershipGraceDays = settings?.MembershipGraceDays ?? DefaultMembershipGraceDays,
            MembershipDebtBehavior = settings?.MembershipDebtBehavior ?? DefaultMembershipDebtBehavior,
            MembershipAutoEndAfterUnpaidPeriods = settings?.MembershipAutoEndAfterUnpaidPeriods,
            MembershipLimitExceededBehavior = settings?.MembershipLimitExceededBehavior ?? MembershipLimitExceededBehavior.FallbackToNextSource,
            CancellationReasonRequiredClient = settings?.CancellationReasonRequiredClient ?? false,
            CancellationReasonRequiredBusiness = settings?.CancellationReasonRequiredBusiness ?? false,
            CancellationReasonRequiredNoShow = settings?.CancellationReasonRequiredNoShow ?? false
        };
    }

    public async Task<OrganizationSettingsDto> UpdateCancellationReasonRules(
        Guid organizationId, Guid userId, OrganizationCancellationReasonRulesUpdateRequest request)
    {
        if (request == null)
            throw new ValidationAppException("Zahtjev je obavezan.");
        await _handler.Upsert(organizationId, userId, settings =>
        {
            settings.CancellationReasonRequiredClient = request.RequiredForClientCancellation;
            settings.CancellationReasonRequiredBusiness = request.RequiredForBusinessCancellation;
            settings.CancellationReasonRequiredNoShow = request.RequiredForNoShow;
        });
        return await GetSettings(organizationId);
    }

    /// <summary>P2 (2F, Vagaro) — postavke provizija (samo razina organizacije, Q40); izlažu se pod commissions.manage jer izravno
    /// mijenjaju zaradu zaposlenika.</summary>
    public async Task<OrganizationCommissionSettingsDto> GetCommissionSettings(Guid organizationId)
    {
        OrganizationSettings settings = await _handler.GetByOrganizationId(organizationId);
        return new OrganizationCommissionSettingsDto
        {
            DeductDiscounts = settings?.CommissionDeductDiscounts ?? false,
            DeductMembershipDiscounts = settings?.CommissionDeductMembershipDiscounts ?? false,
            LateCancellation = settings?.CommissionLateCancellation ?? CommissionLateCancellationMode.Never
        };
    }

    /// <summary>Vrijede za provizije koje nastanu nakon promjene; zarađene provizije se ne mijenjaju (snapshot).</summary>
    public async Task<OrganizationCommissionSettingsDto> UpdateCommissionSettings(Guid organizationId, Guid userId, OrganizationCommissionSettingsDto request)
    {
        CommissionLateCancellationMode lateCancellation = request?.LateCancellation
            ?? throw new ValidationAppException("Postavka provizije kod kasnog otkaza / izostanka je obavezna.");
        await _handler.Upsert(organizationId, userId, settings =>
        {
            settings.CommissionDeductDiscounts = request.DeductDiscounts;
            settings.CommissionDeductMembershipDiscounts = request.DeductMembershipDiscounts;
            settings.CommissionLateCancellation = lateCancellation;
        });
        OrganizationCommissionSettingsDto result = await GetCommissionSettings(organizationId);
        // T1-10: uz isključen "oduzmi popuste članstva" provizija za posjete pokrivene članarinom računa se od cijene sesije i nije
        // ograničena na primljeni iznos (jedina iznimka načela, izričit izbor studija) — studio to mora znati pri spremanju.
        // T1-11: isto za sesije pokrivene neograničenim paketom (obračun kao članarina).
        if (!result.DeductMembershipDiscounts)
            result.Warnings.Add(new WarningDto(WarningCodes.CommissionMembershipSessionsAtListPrice, new WarningCommissionListPriceSessionsDetails
            {
                AppliesTo = new List<string> { "Membership", "UnlimitedPackage" }
            }));
        return result;
    }

    public async Task<OrganizationSettingsDto> UpdateMembershipCoverageRules(
        Guid organizationId, Guid userId, OrganizationMembershipCoverageUpdateRequest request)
    {
        MembershipLimitExceededBehavior behavior = request?.LimitExceededBehavior
            ?? throw new ValidationAppException("Ponašanje kod iskorištenog limita je obavezno.");
        await _handler.Upsert(organizationId, userId, settings => settings.MembershipLimitExceededBehavior = behavior);
        return await GetSettings(organizationId);
    }

    /// <summary>P2 (Q15) — defaulti pravila duga kad redak postavki ne postoji.</summary>
    public const int DefaultMembershipGraceDays = 7;
    public const MembershipDebtBehavior DefaultMembershipDebtBehavior = MembershipDebtBehavior.StopCovering;

    public async Task<OrganizationSettingsDto> UpdateMembershipDebtRules(Guid organizationId, Guid userId, OrganizationMembershipDebtUpdateRequest request)
    {
        if (request == null || request.GraceDays is < 0 or > 365)
            throw new ValidationAppException("Grace period mora biti od 0 do 365 dana.");
        MembershipDebtBehavior behavior = request.DebtBehavior ?? throw new ValidationAppException("Ponašanje kod duga je obavezno.");
        if (request.AutoEndAfterUnpaidPeriods is < 1)
            throw new ValidationAppException("Automatski završetak traži barem 1 neplaćeni period (ili prazno za isključeno).");

        await _handler.Upsert(organizationId, userId, settings =>
        {
            settings.MembershipGraceDays = request.GraceDays;
            settings.MembershipDebtBehavior = behavior;
            settings.MembershipAutoEndAfterUnpaidPeriods = request.AutoEndAfterUnpaidPeriods;
        });
        return await GetSettings(organizationId);
    }

    public async Task<OrganizationSettingsDto> UpdateMembershipChangeNoticeDays(
        Guid organizationId, Guid userId, OrganizationMembershipChangeNoticeUpdateRequest request)
    {
        int days = request?.Days ?? -1;
        if (days is < 0 or > 365)
            throw new ValidationAppException("Rok najave mora biti od 0 do 365 dana.");

        await _handler.Upsert(organizationId, userId, settings => settings.MembershipChangeNoticeDays = days);
        OrganizationSettingsDto result = await GetSettings(organizationId);

        // Pregled 2B (#7) — kratak rok znači da se nepovoljne izmjene plana primjenjuju bez ili s kratkom najavom.
        if (days < ShortMembershipChangeNoticeDays)
            result.Warnings.Add(new WarningDto(WarningCodes.MembershipChangeNoticeShort, new WarningMembershipChangeNoticeDetails
            {
                Days = days,
                RecommendedMinimumDays = ShortMembershipChangeNoticeDays
            }));
        return result;
    }

    /// <summary>Pregled 2B (#7) — rok najave ispod ovoga vraća upozorenje.</summary>
    public const int ShortMembershipChangeNoticeDays = 14;

    public async Task<OrganizationSettingsDto> UpdateTimeZone(Guid organizationId, Guid userId, OrganizationTimeZoneUpdateRequest request)
    {
        string timeZone = request?.TimeZone;
        if (!OrganizationTimeZones.IsSupported(timeZone))
            throw new ValidationAppException($"Nepodržana vremenska zona '{timeZone}' — očekuje se IANA oznaka, npr. \"Europe/Zagreb\".");

        if (!await _handler.UpdateTimeZone(organizationId, timeZone))
            throw new NotFoundAppException("Organization", organizationId);

        return await GetSettings(organizationId);
    }
}
