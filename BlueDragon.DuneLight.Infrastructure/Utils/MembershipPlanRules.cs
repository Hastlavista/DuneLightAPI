using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Jedan limit korištenja plana neovisno o izvoru (zahtjev ili spremljena verzija).</summary>
public readonly record struct MembershipLimitSpec(Guid? ServiceId, MembershipUsageWindow Window, int MaxUses);

/// <summary>
/// P2 (faza 2A) — čista pravila valjanosti uvjeta plana članarine (bez I/O). Strukturne greške bacaju ValidationAppException
/// (400); limiti bez učinka su upozorenja (Q49). Postojanje i aktivnost usluga/poslovnica provjerava servis.
/// Pravila: Q3 (kalendarski način samo uz mjesečni interval), Q5/Q12 (pauza po danima ili periodima prema načinu obnove),
/// Q13 (limiti plana i usluge), Q17/Q49 (prozori prema duljini perioda), Q29 (eksplicitni opseg poslovnica), 2A (barem jedna
/// pokrivena usluga), pregled 2A (dopuštena pauza ima obavezno najveće trajanje).
/// </summary>
public static class MembershipPlanRules
{
    public static void Validate(MembershipPlanTermsRequest terms)
    {
        if (terms == null)
            throw new ValidationAppException("Uvjeti plana su obavezni.");

        EnsureMoney(terms.Price, "Cijena");
        EnsureMoney(terms.StartFee, "Početna naknada");

        MembershipBillingInterval interval = terms.BillingInterval ?? throw new ValidationAppException("Interval naplate je obavezan.");
        MembershipRenewalAnchor anchor = terms.RenewalAnchor ?? throw new ValidationAppException("Način obnove je obavezan.");
        if (anchor == MembershipRenewalAnchor.CalendarMonth && interval != MembershipBillingInterval.Monthly)
            throw new ValidationAppException("Kalendarski način obnove dopušten je samo uz mjesečni interval.");

        ValidateCompanies(terms);
        bool hasBenefits = ValidateBenefits(terms.PriceBenefits);
        List<Guid> serviceIds = ValidateServices(terms, hasBenefits);
        if (serviceIds.Count == 0 && (terms.UsageLimits?.Count ?? 0) > 0)
            throw new ValidationAppException("Limiti korištenja traže barem jednu pokrivenu uslugu.");
        ValidateLimits(terms.UsageLimits, serviceIds, interval);

        if (terms.MinimumCommitmentPeriods is < 1)
            throw new ValidationAppException("Minimalna obveza mora biti barem 1 period (ili prazno ako je nema).");
        if (terms.CancellationNoticeDays is < 1)
            throw new ValidationAppException("Otkazni rok mora biti barem 1 dan (ili prazno ako ga nema).");

        ValidatePause(terms.Pause, anchor);
    }

    /// <summary>Q49 t.3 — limiti bez učinka: dulji prozor s limitom &gt;= kredit perioda × broj perioda u prozoru, te limit
    /// usluge &gt;= limit cijelog plana za isti prozor.</summary>
    public static List<WarningDto> LimitWarnings(MembershipBillingInterval interval, IReadOnlyCollection<MembershipLimitSpec> limits)
    {
        List<WarningDto> warnings = new();
        foreach (MembershipLimitSpec limit in limits.Where(l => l.Window != MembershipUsageWindow.Period))
        {
            MembershipLimitSpec? credits = ReferenceCredits(limits, limit.ServiceId);
            if (credits == null || Compare(limit.Window, interval) != WindowLength.Longer)
                continue;

            int periodsInWindow = WindowMonths(limit.Window) / PeriodMonths(interval);
            if (limit.MaxUses >= credits.Value.MaxUses * periodsInWindow)
                warnings.Add(WithoutEffect(limit, credits.Value));
        }

        foreach (MembershipLimitSpec serviceLimit in limits.Where(l => l.ServiceId != null))
        {
            MembershipLimitSpec planLimit = limits.FirstOrDefault(l => l.ServiceId == null && l.Window == serviceLimit.Window);
            if (planLimit != default && serviceLimit.MaxUses >= planLimit.MaxUses)
                warnings.Add(WithoutEffect(serviceLimit, planLimit));
        }

        return warnings;
    }

    private static void ValidateCompanies(MembershipPlanTermsRequest terms)
    {
        MembershipCompanyScope scope = terms.CompanyScope ?? throw new ValidationAppException("Opseg poslovnica je obavezan.");
        List<Guid> companyIds = terms.CompanyIds ?? new List<Guid>();
        if (companyIds.Count != companyIds.Distinct().Count())
            throw new ValidationAppException("Poslovnica je navedena više puta.");

        if (scope == MembershipCompanyScope.SelectedCompanies && companyIds.Count == 0)
            throw new ValidationAppException(ErrorCodes.MembershipPlanCompaniesRequired,
                "Plan s odabranim poslovnicama mora imati barem jednu poslovnicu (prazna lista ne znači sve poslovnice).");
        if (scope == MembershipCompanyScope.AllCompanies && companyIds.Count > 0)
            throw new ValidationAppException("Plan koji vrijedi u svim poslovnicama ne navodi pojedine poslovnice.");
    }

    /// <remarks>2E (dnevnik 2A): plan mora pokrivati barem jednu uslugu ILI imati cjenovnu pogodnost (članarina samo za popust).</remarks>
    private static List<Guid> ValidateServices(MembershipPlanTermsRequest terms, bool hasBenefits)
    {
        List<MembershipPlanCoveredServiceRequest> services = terms.Services ?? new List<MembershipPlanCoveredServiceRequest>();
        if (services.Count == 0 && !hasBenefits)
            throw new ValidationAppException("Plan mora pokrivati barem jednu uslugu ili imati cjenovnu pogodnost za članove.");
        if (services.Any(s => s?.ServiceId == null))
            throw new ValidationAppException("Svaka pokrivena usluga mora imati ServiceId.");

        List<Guid> ids = services.Select(s => s.ServiceId.Value).ToList();
        if (ids.Count != ids.Distinct().Count())
            throw new ValidationAppException("Pokrivena usluga je navedena više puta.");
        return ids;
    }

    private static void ValidateLimits(List<MembershipUsageLimitDto> requested, List<Guid> serviceIds, MembershipBillingInterval interval)
    {
        List<MembershipLimitSpec> limits = new();
        foreach (MembershipUsageLimitDto limit in requested ?? new List<MembershipUsageLimitDto>())
        {
            if (limit?.Window == null)
                throw new ValidationAppException("Limit korištenja mora imati prozor.");
            if (limit.MaxUses < 1)
                throw new ValidationAppException("Limit korištenja mora biti barem 1.");
            if (limit.ServiceId.HasValue && !serviceIds.Contains(limit.ServiceId.Value))
                throw new ValidationAppException("Limit usluge se odnosi na uslugu koju plan ne pokriva.");
            if (limits.Any(l => l.ServiceId == limit.ServiceId && l.Window == limit.Window.Value))
                throw new ValidationAppException("Limit za isti opseg i prozor je naveden više puta.");
            limits.Add(new MembershipLimitSpec(limit.ServiceId, limit.Window.Value, limit.MaxUses));
        }

        // Q49 — prozor prema duljini perioda, uspoređeno s kreditima istog opsega (usluga → krediti usluge, inače plana;
        // plan → krediti plana). Bez kredita (neograničeno) svi prozori su dopušteni.
        foreach (MembershipLimitSpec limit in limits.Where(l => l.Window != MembershipUsageWindow.Period))
        {
            MembershipLimitSpec? credits = ReferenceCredits(limits, limit.ServiceId);
            if (credits == null)
                continue;

            switch (Compare(limit.Window, interval))
            {
                case WindowLength.Same:
                    throw new ValidationAppException(ErrorCodes.MembershipUsageLimitInvalid,
                        $"Prozor {limit.Window} jednak je periodu plana; uz kredite perioda nije dopušten.");
                case WindowLength.Shorter when limit.MaxUses >= credits.Value.MaxUses:
                    throw new ValidationAppException(ErrorCodes.MembershipUsageLimitInvalid,
                        $"Limit prozora {limit.Window} mora biti manji od kredita perioda ({credits.Value.MaxUses}).");
                case WindowLength.Longer when limit.MaxUses <= credits.Value.MaxUses:
                    throw new ValidationAppException(ErrorCodes.MembershipUsageLimitInvalid,
                        $"Limit prozora {limit.Window} mora biti veći od kredita perioda ({credits.Value.MaxUses}).");
            }
        }
    }

    private static void ValidatePause(MembershipPauseRulesDto pause, MembershipRenewalAnchor anchor)
    {
        if (pause == null)
            throw new ValidationAppException("Pravila pauze su obavezna.");

        if (!pause.Allowed)
        {
            if (pause.MaxPauseDays.HasValue || pause.MaxPausePeriods.HasValue || pause.MaxPausesPer12Months.HasValue || pause.ExtendsPeriod)
                throw new ValidationAppException("Plan bez pauze ne navodi pravila pauze.");
            return;
        }

        if (pause.MaxPauseDays is < 1 || pause.MaxPausePeriods is < 1 || pause.MaxPausesPer12Months is < 1)
            throw new ValidationAppException("Ograničenja pauze moraju biti barem 1 (ili prazno za bez ograničenja).");

        // Q5.1 — "od datuma kupnje" pauzira po danima; kalendarski samo u cijelim periodima (preskok), bez produljenja.
        if (anchor == MembershipRenewalAnchor.PurchaseDate && pause.MaxPausePeriods.HasValue)
            throw new ValidationAppException("Plan s obnovom od datuma kupnje pauzira po danima (MaxPauseDays), ne po periodima.");
        if (anchor == MembershipRenewalAnchor.CalendarMonth && (pause.MaxPauseDays.HasValue || pause.ExtendsPeriod))
            throw new ValidationAppException("Kalendarski plan pauzira samo u cijelim periodima (MaxPausePeriods), bez produljenja po danima.");

        // Pregled 2A — trajanje pauze je obavezno: neograničena pauza uz minimalnu obvezu koja se broji samo izvan pauze bi
        // omogućila neograničeno izbjegavanje obveze. Broj pauza u 12 mjeseci smije ostati neograničen.
        if (anchor == MembershipRenewalAnchor.PurchaseDate && !pause.MaxPauseDays.HasValue)
            throw new ValidationAppException("Plan s dopuštenom pauzom mora imati najveće trajanje pauze u danima (MaxPauseDays).");
        if (anchor == MembershipRenewalAnchor.CalendarMonth && !pause.MaxPausePeriods.HasValue)
            throw new ValidationAppException("Kalendarski plan s dopuštenom pauzom mora imati najveći broj pauziranih perioda (MaxPausePeriods).");
    }

    /// <summary>2E — pravila cjenovne pogodnosti: izričit opseg (AllServices bez usluge, Service s uslugom; prazna usluga nikad ne
    /// znači "sve"), najviše jedno pravilo po usluzi i jedno AllServices, vrijednost prema tipu (najviše 2 decimale). Vraća ima li
    /// plan pogodnost.</summary>
    private static bool ValidateBenefits(List<MembershipPriceBenefitDto> benefits)
    {
        benefits ??= new List<MembershipPriceBenefitDto>();
        foreach (MembershipPriceBenefitDto benefit in benefits)
        {
            if (benefit?.Scope == null || benefit.Type == null)
                throw new ValidationAppException("Pravilo pogodnosti mora imati opseg i vrstu.");
            if (benefit.Scope == MembershipPriceBenefitScope.Service && benefit.ServiceId == null)
                throw new ValidationAppException("Pravilo pogodnosti za odabranu uslugu mora navesti uslugu (prazna usluga ne znači \"sve usluge\").");
            if (benefit.Scope == MembershipPriceBenefitScope.AllServices && benefit.ServiceId != null)
                throw new ValidationAppException("Pravilo pogodnosti za sve usluge ne navodi uslugu.");
            if (decimal.Round(benefit.Value, 2) != benefit.Value)
                throw new ValidationAppException("Vrijednost pogodnosti smije imati najviše 2 decimale.");
            switch (benefit.Type.Value)
            {
                case MembershipPriceBenefitType.PercentOff when benefit.Value <= 0m || benefit.Value > 100m:
                    throw new ValidationAppException("Postotak popusta mora biti veći od 0 i najviše 100.");
                case MembershipPriceBenefitType.AmountOff when benefit.Value <= 0m:
                    throw new ValidationAppException("Iznos popusta mora biti veći od 0.");
                case MembershipPriceBenefitType.FixedPrice when benefit.Value < 0m:
                    throw new ValidationAppException("Cijena za člana ne smije biti negativna.");
            }
        }

        if (benefits.Count(b => b.Scope == MembershipPriceBenefitScope.AllServices) > 1)
            throw new ValidationAppException("Plan smije imati najviše jedno pravilo pogodnosti za sve usluge.");
        List<Guid> serviceIds = benefits.Where(b => b.ServiceId.HasValue).Select(b => b.ServiceId.Value).ToList();
        if (serviceIds.Count != serviceIds.Distinct().Count())
            throw new ValidationAppException("Plan smije imati najviše jedno pravilo pogodnosti po usluzi.");
        return benefits.Count > 0;
    }

    private static void EnsureMoney(decimal amount, string label)
    {
        if (amount < 0m)
            throw new ValidationAppException($"{label} ne smije biti negativna.");
        if (decimal.Round(amount, 2) != amount)
            throw new ValidationAppException($"{label} smije imati najviše 2 decimale.");
    }

    private static MembershipLimitSpec? ReferenceCredits(IEnumerable<MembershipLimitSpec> limits, Guid? serviceId)
    {
        List<MembershipLimitSpec> periodLimits = limits.Where(l => l.Window == MembershipUsageWindow.Period).ToList();
        if (serviceId.HasValue)
        {
            MembershipLimitSpec serviceCredits = periodLimits.FirstOrDefault(l => l.ServiceId == serviceId);
            if (serviceCredits != default)
                return serviceCredits;
        }

        MembershipLimitSpec planCredits = periodLimits.FirstOrDefault(l => l.ServiceId == null);
        return planCredits == default ? null : planCredits;
    }

    private enum WindowLength { Shorter, Same, Longer }

    private static WindowLength Compare(MembershipUsageWindow window, MembershipBillingInterval interval)
    {
        if (window is MembershipUsageWindow.Day or MembershipUsageWindow.Week)
            return WindowLength.Shorter;
        int windowMonths = WindowMonths(window);
        int periodMonths = PeriodMonths(interval);
        return windowMonths < periodMonths ? WindowLength.Shorter : windowMonths == periodMonths ? WindowLength.Same : WindowLength.Longer;
    }

    private static int WindowMonths(MembershipUsageWindow window) => window switch
    {
        MembershipUsageWindow.Month => 1,
        MembershipUsageWindow.Quarter => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Prozor nema duljinu u mjesecima.")
    };

    private static int PeriodMonths(MembershipBillingInterval interval) => interval switch
    {
        MembershipBillingInterval.Monthly => 1,
        MembershipBillingInterval.Yearly => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(interval))
    };

    private static WarningDto WithoutEffect(MembershipLimitSpec limit, MembershipLimitSpec comparedWith) =>
        new(WarningCodes.MembershipLimitWithoutEffect, new WarningMembershipLimitDetails
        {
            ServiceId = limit.ServiceId,
            Window = limit.Window.ToString(),
            MaxUses = limit.MaxUses,
            ComparedWithServiceId = comparedWith.ServiceId,
            ComparedWithWindow = comparedWith.Window.ToString(),
            ComparedWithMaxUses = comparedWith.MaxUses
        });
}
