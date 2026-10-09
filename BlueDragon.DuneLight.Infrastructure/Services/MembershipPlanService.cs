using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// P2 (faza 2A, docs/p2) — katalog planova članarina. Uvjeti su u nepromjenjivim verzijama (objava = Version + 1 pod lockom
/// plana, isti obrazac kao CancellationPolicyService). Reference: nova usluga/poslovnica mora postojati u organizaciji i biti
/// aktivna; ona koja je već bila u najnovijoj verziji smije ostati i kad je u međuvremenu deaktivirana (grandfathering, isti
/// obrazac kao ClientService/EmployeeService). Nova verzija vrijedi za nove prodaje; uz ApplyTo = NewSalesAndExisting (Q14)
/// postojeća članstva je dobivaju od prve obnove nakon roka najave organizacije, a strogo povoljnu izmjenu (Q48) od sljedeće
/// obnove. Lock redoslijed objave: plan → članstva plana.
/// </summary>
public class MembershipPlanService : IMembershipPlanService
{
    private readonly IMembershipPlanHandler _handler;
    private readonly IServiceHandler _serviceHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IClientMembershipHandler _membershipHandler;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;
    private readonly IMembershipCoverageService _coverage;
    private readonly IPricingService _pricingService;
    private readonly TimeProvider _timeProvider;

    public MembershipPlanService(
        IMembershipPlanHandler handler,
        IServiceHandler serviceHandler,
        ICompanyHandler companyHandler,
        IClientMembershipHandler membershipHandler,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService,
        IUnitOfWorkFactory unitOfWorkFactory,
        IMembershipCoverageService coverage,
        IPricingService pricingService,
        TimeProvider timeProvider)
    {
        _pricingService = pricingService;
        _handler = handler;
        _serviceHandler = serviceHandler;
        _companyHandler = companyHandler;
        _membershipHandler = membershipHandler;
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _unitOfWorkFactory = unitOfWorkFactory;
        _coverage = coverage;
        _timeProvider = timeProvider;
    }

    public async Task<List<MembershipPlanDto>> GetAll(Guid organizationId)
    {
        List<MembershipPlan> plans = await _handler.GetAll(organizationId);
        List<MembershipPlanDto> result = new();
        foreach (MembershipPlan plan in plans)
            result.Add(await WithBenefitWarnings(organizationId, plan, ToDto(plan, includeVersions: false)));
        return result;
    }

    public async Task<MembershipPlanDto> GetById(Guid organizationId, Guid id)
    {
        MembershipPlan plan = await _handler.GetById(organizationId, id)
            ?? throw new NotFoundAppException("MembershipPlan", id);
        return await WithBenefitWarnings(organizationId, plan, ToDto(plan, includeVersions: true));
    }

    public async Task<MembershipPlanDto> Create(Guid organizationId, Guid userId, MembershipPlanCreateRequest request)
    {
        string name = NormalizeName(request?.Name);
        EnsureCapacity(request.MaxActiveMemberships);
        MembershipPlanRules.Validate(request);
        await EnsureReferences(organizationId, request, grandfathered: null);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        MembershipPlan plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            Description = NormalizeDescription(request.Description),
            IsActive = true,
            MaxActiveMemberships = request.MaxActiveMemberships,
            CreatedAt = now,
            CreatedBy = userId
        };

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            await EnsureActiveNameFree(uow, organizationId, name, exceptId: null);
            uow.Context.MembershipPlans.Add(plan);
            uow.Context.MembershipPlanVersions.Add(NewVersion(plan, 1, request, userId, now));
            await SaveTranslatingDuplicates(uow, name);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, plan.Id);
    }

    public async Task<MembershipPlanDto> UpdateDetails(Guid organizationId, Guid userId, Guid id, MembershipPlanDetailsRequest request)
    {
        string name = NormalizeName(request?.Name);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            MembershipPlan plan = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("MembershipPlan", id);
            if (plan.IsActive)
                await EnsureActiveNameFree(uow, organizationId, name, exceptId: id);
            plan.Name = name;
            plan.Description = NormalizeDescription(request.Description);
            Touch(plan, userId, _timeProvider.GetUtcNow());
            await SaveTranslatingDuplicates(uow, name);
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<MembershipPlanDto> UpdateCapacity(Guid organizationId, Guid userId, Guid id, MembershipPlanCapacityRequest request)
    {
        EnsureCapacity(request?.MaxActiveMemberships);
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            MembershipPlan plan = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("MembershipPlan", id);
            plan.MaxActiveMemberships = request?.MaxActiveMemberships;
            Touch(plan, userId, _timeProvider.GetUtcNow());
            await uow.Context.SaveChangesAsync();
            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    public async Task<MembershipPlanVersionPublishResultDto> PublishVersion(
        Guid organizationId, Guid userId, Guid id, MembershipPlanVersionPublishRequest request)
    {
        MembershipPlanRules.Validate(request);
        MembershipPlanVersionPublishResultDto result = new() { ApplyTo = request.ApplyTo };
        DateOnly today = (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());
        int noticeDays = await _organizationSettingsService.GetMembershipChangeNoticeDays(organizationId);

        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            MembershipPlan plan = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("MembershipPlan", id);

            // Version + 1 pod lockom plana; starije verzije se nikad ne mijenjaju.
            MembershipPlanVersion latest = await _handler.GetLatestVersion(uow, id);
            await EnsureReferences(organizationId, request, latest);

            DateTimeOffset now = _timeProvider.GetUtcNow();
            MembershipPlanVersion version = NewVersion(plan, (latest?.Version ?? 0) + 1, request, userId, now);
            uow.Context.MembershipPlanVersions.Add(version);
            Touch(plan, userId, now);

            (result.Classification, result.WorsenedDimensions) = latest == null
                ? (MembershipPlanChangeClassification.Favorable, new List<string>())
                : MembershipPlanChangeClassifier.Classify(latest, version);

            if (request.ApplyTo == MembershipPlanApplyTo.NewSalesAndExisting)
                result.AffectedMemberships = await ScheduleForExistingMemberships(
                    uow, organizationId, userId, plan, version, result.Classification, today, noticeDays);

            await uow.Context.SaveChangesAsync();
            // 2D: termini od datuma stupanja novih uvjeta na snagu evaluiraju se po njima (usluge, poslovnice, limiti).
            foreach (MembershipPlanAffectedMembershipDto affected in (result.AffectedMemberships ?? new()).Where(a => !a.Skipped))
                await _coverage.ReconcileMembership(uow, organizationId, affected.MembershipId, MembershipCoverageEvent.MembershipChanged, userId);
            await uow.CommitAsync();
        }

        result.Plan = await GetById(organizationId, id);
        return result;
    }

    /// <summary>Q14/Q48 — postojeća članstva plana dobivaju novu verziju od prve obnove na ili nakon (danas + rok najave), a
    /// strogo povoljnu izmjenu od sljedeće obnove. Članstvo koje završava prije te obnove se ne mijenja. Snapshot (trenutna
    /// verzija) se nikad ne mijenja retroaktivno.
    /// Pregled 2B: (1) članstvo kojem bi nova verzija stvorila preklapanje s drugom članarinom klijenta (Q10) se NE mijenja i
    /// dobiva TRAJNU oznaku (verzija + razlog) dok je kasnija uspješna izmjena ne riješi — bez automatske ponovne primjene;
    /// (2) dok čeka klijentova promjena plana (ima prednost), izmjena se ne primjenjuje nego se pamti kao istisnuta s izvornim
    /// datumom, pa je povlačenje promjene vraća točno kao da promjene nije bilo.</summary>
    private async Task<List<MembershipPlanAffectedMembershipDto>> ScheduleForExistingMemberships(
        IUnitOfWork uow, Guid organizationId, Guid userId, MembershipPlan plan, MembershipPlanVersion version,
        MembershipPlanChangeClassification classification, DateOnly today, int noticeDays)
    {
        List<MembershipPlanAffectedMembershipDto> affected = new();
        List<ClientMembership> memberships = await _membershipHandler.GetActiveForPlanForUpdate(uow, organizationId, plan.Id, today);
        DateOnly notBefore = classification == MembershipPlanChangeClassification.Favorable ? today.AddDays(1) : today.AddDays(noticeDays);

        foreach (ClientMembership membership in memberships)
        {
            if (membership.PlanVersionId == version.Id)
                continue;

            MembershipTimeline timeline = MembershipTimelines.Current(membership);
            DateOnly from = notBefore > membership.StartsOn ? notBefore : membership.StartsOn.AddDays(1);
            DateOnly effectiveOn = MembershipPeriodCalendar.FirstPeriodStartingOnOrAfter(timeline, from).StartsOn;
            if (membership.EndsOn.HasValue && effectiveOn > membership.EndsOn.Value)
                continue;

            MembershipPlanAffectedMembershipDto entry = new() { MembershipId = membership.Id, ClientId = membership.ClientId, EffectiveOn = effectiveOn };
            affected.Add(entry);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            membership.UpdatedAt = now;
            membership.UpdatedBy = userId;

            List<ClientMembership> others = await _membershipHandler.GetCoverageCandidates(uow, organizationId, membership.ClientId);
            MembershipCoverageScope candidate = MembershipTimelines.Scope(membership.Id, plan.Name, effectiveOn, membership.EndsOn, version);
            if (MembershipCoverageOverlap.FindConflict(candidate, others.SelectMany(MembershipTimelines.Scopes)) != null)
            {
                (entry.Skipped, entry.SkipReason) = (true, ErrorCodes.MembershipOverlappingCoverage);
                MembershipTimelines.MarkPlanUpdateNotApplied(membership, version.Id, ErrorCodes.MembershipOverlappingCoverage, now);
                _membershipHandler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanUpdateNotApplied", null,
                    version.Id.ToString(), now, ErrorCodes.MembershipOverlappingCoverage));
                continue;
            }

            MembershipTimelines.ClearPlanUpdateNotApplied(membership);
            if (membership.PendingSource == MembershipPendingChangeSource.ClientPlanChange)
            {
                // Klijentova promjena ima prednost; izmjena se pamti s izvornim datumom za slučaj povlačenja promjene.
                entry.HeldByClientPlanChange = true;
                string oldDisplaced = membership.DisplacedPlanVersionId?.ToString();
                membership.DisplacedPlanVersionId = version.Id;
                membership.DisplacedEffectiveOn = effectiveOn;
                _membershipHandler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanUpdateHeldByClientPlanChange", oldDisplaced,
                    $"{version.Id}@{effectiveOn:yyyy-MM-dd}", now, classification.ToString()));
                continue;
            }

            string old = membership.PendingPlanVersionId?.ToString();
            membership.PendingPlanVersionId = version.Id;
            membership.PendingEffectiveOn = effectiveOn;
            membership.PendingSource = MembershipPendingChangeSource.PlanUpdate;
            _membershipHandler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "PlanUpdateScheduled", old,
                $"{version.Id}@{effectiveOn:yyyy-MM-dd}", now, classification.ToString()));
        }

        return affected;
    }

    public async Task<MembershipPlanDto> Activate(Guid organizationId, Guid userId, Guid id)
    {
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            MembershipPlan plan = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("MembershipPlan", id);
            if (!plan.IsActive)
            {
                await EnsureActiveNameFree(uow, organizationId, plan.Name, exceptId: id);
                plan.IsActive = true;
                Touch(plan, userId, _timeProvider.GetUtcNow());
                await SaveTranslatingDuplicates(uow, plan.Name);
            }

            await uow.CommitAsync();
        }

        return await GetById(organizationId, id);
    }

    /// <summary>Deaktiviran plan se ne prodaje; postojeća članstva traju do kraja tekućeg perioda i ne obnavljaju se
    /// (provodi obnova u 2C).</summary>
    public async Task<MembershipPlanDto> Deactivate(Guid organizationId, Guid userId, Guid id)
    {
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            MembershipPlan plan = await _handler.GetForUpdate(uow, organizationId, id)
                ?? throw new NotFoundAppException("MembershipPlan", id);
            if (plan.IsActive)
            {
                plan.IsActive = false;
                Touch(plan, userId, _timeProvider.GetUtcNow());
                await uow.Context.SaveChangesAsync();
            }

            await uow.CommitAsync();
        }

        // 2C — članstva završavaju na sljedećoj obnovi ako plan tada još nije aktivan; odgovor nosi broj, popis je na
        // GET /api/membership-plans/{id}/memberships-ending.
        MembershipPlanDto result = await GetById(organizationId, id);
        DateOnly today = (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());
        int ending = (await _membershipHandler.GetActiveForPlan(organizationId, id, today)).Count(ClientMembershipService.WillEndDueToDeactivation);
        if (ending > 0)
            result.Warnings.Add(new WarningDto(WarningCodes.MembershipPlanMembershipsEnding, new WarningMembershipsEndingDetails { Count = ending }));
        return result;
    }

    #region Pomoćno

    /// <summary>Usluge i poslovnice moraju postojati u organizaciji (inače NotFound, i za tuđi tenant); nova referenca mora biti
    /// aktivna, a ona iz najnovije verzije smije ostati neaktivna (grandfathering).</summary>
    private async Task EnsureReferences(Guid organizationId, MembershipPlanTermsRequest terms, MembershipPlanVersion grandfathered)
    {
        // 2E: i usluge pravila pogodnosti moraju postojati i biti aktivne (osim već postojećih u prethodnoj verziji).
        List<Guid> serviceIds = (terms.Services ?? new List<MembershipPlanCoveredServiceRequest>()).Select(s => s.ServiceId.GetValueOrDefault())
            .Concat((terms.PriceBenefits ?? new List<MembershipPriceBenefitDto>()).Where(b => b.ServiceId.HasValue).Select(b => b.ServiceId.Value))
            .Distinct().ToList();
        HashSet<Guid> grandfatheredServices = (grandfathered?.Services.Select(s => s.ServiceId) ?? Enumerable.Empty<Guid>())
            .Concat(grandfathered?.PriceBenefits.Where(b => b.ServiceId.HasValue).Select(b => b.ServiceId.Value) ?? Enumerable.Empty<Guid>())
            .ToHashSet();
        List<ServiceEntity> services = await _serviceHandler.GetByIds(organizationId, serviceIds);
        foreach (Guid serviceId in serviceIds)
        {
            ServiceEntity service = services.SingleOrDefault(s => s.Id == serviceId)
                ?? throw new NotFoundAppException("Service", serviceId);
            if (!service.IsActive && !grandfatheredServices.Contains(serviceId))
                throw new BusinessRuleException(ErrorCodes.InactiveService, $"Usluga '{service.Name}' nije aktivna i ne može se dodati planu.");
        }

        List<Guid> companyIds = terms.CompanyIds ?? new List<Guid>();
        if (companyIds.Count == 0)
            return;

        HashSet<Guid> grandfatheredCompanies = grandfathered?.Companies.Select(c => c.CompanyId).ToHashSet() ?? new HashSet<Guid>();
        List<Company> companies = await _companyHandler.GetByIds(organizationId, companyIds);
        foreach (Guid companyId in companyIds)
        {
            Company company = companies.SingleOrDefault(c => c.Id == companyId)
                ?? throw new NotFoundAppException("Company", companyId);
            if (!company.IsActive && !grandfatheredCompanies.Contains(companyId))
                throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Poslovnica '{company.Name}' nije aktivna i ne može se dodati planu.");
        }
    }

    private static MembershipPlanVersion NewVersion(
        MembershipPlan plan, int version, MembershipPlanTermsRequest terms, Guid userId, DateTimeOffset now)
    {
        Guid versionId = Guid.NewGuid();
        Guid organizationId = plan.OrganizationId;
        return new MembershipPlanVersion
        {
            Id = versionId,
            OrganizationId = organizationId,
            MembershipPlanId = plan.Id,
            Version = version,
            Price = terms.Price,
            StartFee = terms.StartFee,
            BillingInterval = terms.BillingInterval.GetValueOrDefault(),
            RenewalAnchor = terms.RenewalAnchor.GetValueOrDefault(),
            CompanyScope = terms.CompanyScope.GetValueOrDefault(),
            MinimumCommitmentPeriods = terms.MinimumCommitmentPeriods,
            CancellationNoticeDays = terms.CancellationNoticeDays,
            PauseAllowed = terms.Pause.Allowed,
            MaxPauseDays = terms.Pause.MaxPauseDays,
            MaxPausePeriods = terms.Pause.MaxPausePeriods,
            MaxPausesPer12Months = terms.Pause.MaxPausesPer12Months,
            PauseExtendsPeriod = terms.Pause.ExtendsPeriod,
            CreatedAt = now,
            CreatedBy = userId,
            Services = (terms.Services ?? new List<MembershipPlanCoveredServiceRequest>()).Select(s => new MembershipPlanVersionService
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MembershipPlanVersionId = versionId,
                ServiceId = s.ServiceId.GetValueOrDefault()
            }).ToList(),
            Companies = (terms.CompanyIds ?? new List<Guid>()).Select(companyId => new MembershipPlanVersionCompany
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MembershipPlanVersionId = versionId,
                CompanyId = companyId
            }).ToList(),
            UsageLimits = (terms.UsageLimits ?? new List<MembershipUsageLimitDto>()).Select(l => new MembershipPlanUsageLimit
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MembershipPlanVersionId = versionId,
                ServiceId = l.ServiceId,
                Window = l.Window.GetValueOrDefault(),
                MaxUses = l.MaxUses
            }).ToList(),
            PriceBenefits = (terms.PriceBenefits ?? new List<MembershipPriceBenefitDto>()).Select(b => new MembershipPlanPriceBenefit
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                MembershipPlanVersionId = versionId,
                Scope = b.Scope.GetValueOrDefault(),
                ServiceId = b.Scope == MembershipPriceBenefitScope.Service ? b.ServiceId : null,
                Type = b.Type.GetValueOrDefault(),
                Value = b.Value
            }).ToList()
        };
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationAppException("Naziv plana je obavezan.");
        return name.Trim();
    }

    private static string NormalizeDescription(string description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static void EnsureCapacity(int? maxActiveMemberships)
    {
        if (maxActiveMemberships is < 1)
            throw new ValidationAppException("Najveći broj aktivnih članstava mora biti barem 1 (ili prazno za bez ograničenja).");
    }

    private async Task EnsureActiveNameFree(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId)
    {
        if (await _handler.ActiveNameExists(uow, organizationId, name, exceptId))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivan plan članarine s nazivom '{name}' već postoji.");
    }

    private static async Task SaveTranslatingDuplicates(IUnitOfWork uow, string name)
    {
        try
        {
            await uow.Context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Aktivan plan članarine s nazivom '{name}' već postoji.");
        }
    }

    /// <summary>Pregled 2E (#1) — fiksna cijena za člana koja nije niža od cjenika usluge u nekoj poslovnici plana se tamo ne
    /// primjenjuje (NoReduction): upozorenje se računa pri ČITANJU plana nad trenutnim cjenikom (cjenik se mijenja kasnije).
    /// Samo pravila FixedPrice (postotak i iznos popusta uvijek snižavaju pozitivnu cijenu).</summary>
    private async Task<MembershipPlanDto> WithBenefitWarnings(Guid organizationId, MembershipPlan plan, MembershipPlanDto dto)
    {
        MembershipPlanVersion latest = plan.Versions.OrderByDescending(v => v.Version).FirstOrDefault();
        List<MembershipPlanPriceBenefit> fixedPrices = latest?.PriceBenefits.Where(b => b.Type == MembershipPriceBenefitType.FixedPrice).ToList()
                                                      ?? new List<MembershipPlanPriceBenefit>();
        if (fixedPrices.Count == 0)
            return dto;

        List<Guid> companyIds;
        List<Guid> allServiceIds;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            companyIds = latest.CompanyScope == MembershipCompanyScope.AllCompanies
                ? await uow.Context.Companies.Where(c => c.OrganizationId == organizationId && c.IsActive).Select(c => c.Id.Value).ToListAsync()
                : latest.Companies.Select(c => c.CompanyId).ToList();
            allServiceIds = await uow.Context.Services.Where(s => s.OrganizationId == organizationId && s.IsActive).Select(s => s.Id.Value).ToListAsync();
        }

        List<WarningMembershipBenefitPrice> prices = new();
        foreach (MembershipPlanPriceBenefit rule in fixedPrices)
        {
            IEnumerable<Guid> services = rule.Scope == MembershipPriceBenefitScope.Service
                ? new[] { rule.ServiceId.Value }
                : allServiceIds.Where(id => latest.PriceBenefits.All(b => b.ServiceId != id));
            foreach (Guid serviceId in services)
            foreach (Guid companyId in companyIds)
            {
                Core.DTOs.Catalog.ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
                {
                    SubjectType = PricingSubjectType.Service, SubjectId = serviceId, CompanyId = companyId // T1-7: Date null = danas u zoni poslovnice
                });
                if (rule.Value >= resolved.Price)
                    prices.Add(new WarningMembershipBenefitPrice { ServiceId = serviceId, CompanyId = companyId, ListPrice = resolved.Price, MemberPrice = rule.Value });
            }
        }

        if (prices.Count > 0)
            dto.Warnings.Add(new WarningDto(WarningCodes.MembershipBenefitWithoutEffect, new WarningMembershipBenefitDetails { Prices = prices }));
        return dto;
    }

    private static void Touch(MembershipPlan plan, Guid userId, DateTimeOffset now)
    {
        plan.UpdatedAt = now;
        plan.UpdatedBy = userId;
    }

    private static MembershipPlanDto ToDto(MembershipPlan plan, bool includeVersions)
    {
        List<MembershipPlanVersion> ordered = plan.Versions.OrderByDescending(v => v.Version).ToList();
        MembershipPlanVersion latest = ordered.FirstOrDefault();
        return new MembershipPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Description = plan.Description,
            IsActive = plan.IsActive,
            MaxActiveMemberships = plan.MaxActiveMemberships,
            LatestVersion = latest == null ? null : MembershipPlanReadModel.ToDto(latest),
            Versions = includeVersions ? ordered.Select(MembershipPlanReadModel.ToDto).ToList() : new List<MembershipPlanVersionDto>(),
            Warnings = latest == null ? new List<WarningDto>() : MembershipPlanReadModel.WarningsOf(latest),
            CreatedAt = plan.CreatedAt,
            CreatedBy = plan.CreatedBy,
            UpdatedAt = plan.UpdatedAt,
            UpdatedBy = plan.UpdatedBy
        };
    }

    #endregion
}
