using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using ServiceEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog.Service;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class PricingService : IPricingService
{
    private readonly IPriceListItemHandler _priceListItemHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IPriceResolutionService _priceResolutionService;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly TimeProvider _timeProvider;
    private readonly IOrganizationCalendarService _calendars;

    public PricingService(
        IPriceListItemHandler priceListItemHandler,
        IServiceHandler serviceHandler,
        IPackageHandler packageHandler,
        ICompanyHandler companyHandler,
        IPriceResolutionService priceResolutionService,
        IEmployeeHandler employeeHandler,
        TimeProvider timeProvider,
        IOrganizationCalendarService calendars)
    {
        _calendars = calendars;
        _employeeHandler = employeeHandler;
        _priceListItemHandler = priceListItemHandler;
        _serviceHandler = serviceHandler;
        _packageHandler = packageHandler;
        _companyHandler = companyHandler;
        _priceResolutionService = priceResolutionService;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResult<PriceListItemDto>> GetPaged(
        Guid organizationId, PagedRequest request, Guid? companyId, PricingSubjectType? subjectType)
    {
        (List<PriceListItem> items, int totalCount) = await _priceListItemHandler.GetPaged(organizationId, request, companyId, subjectType);
        return PagedResult<PriceListItemDto>.Create(items.Select(ToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<PriceListItemDto> GetById(Guid organizationId, Guid id)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        return ToDto(item);
    }

    public async Task<PriceListItemDto> Create(Guid organizationId, Guid userId, PriceListItemCreateRequest request)
    {
        Guid subjectId = ValidateSubject(request.SubjectType, request.ServiceId, request.PackageId);
        await EnsurePriceEmployee(organizationId, request.SubjectType, request.EmployeeId, requireActive: true);
        await GetDefaultPrice(organizationId, request.SubjectType, subjectId, requireActive: true);
        await EnsureCompanyExists(organizationId, request.CompanyId, requireActive: true);
        ValidateDateRange(request.ValidFrom, request.ValidTo);

        await EnsureNoOverlap(organizationId, request.SubjectType, subjectId, request.CompanyId, request.EmployeeId,
            request.ValidFrom, request.ValidTo, excludeId: null);

        PriceListItem item = new PriceListItem
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ServiceId = request.SubjectType == PricingSubjectType.Service ? subjectId : null,
            PackageId = request.SubjectType == PricingSubjectType.Package ? subjectId : null,
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            Price = request.Price,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = true,
            CreatedAt = _timeProvider.GetUtcNow(),
            CreatedBy = userId
        };

        await _priceListItemHandler.Add(item);
        PriceListItemDto dto = await GetById(organizationId, item.Id.GetValueOrDefault());
        dto.Warnings.AddRange(await SaveWarnings(organizationId, item));
        return dto;
    }

    public async Task<PriceListItemDto> Update(Guid organizationId, Guid userId, Guid id, PriceListItemUpdateRequest request)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        ValidateDateRange(request.ValidFrom, request.ValidTo);

        PricingSubjectType subjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package;
        Guid subjectId = item.ServiceId ?? item.PackageId!.Value;

        if (item.IsActive)
            await EnsureNoOverlap(organizationId, subjectType, subjectId, item.CompanyId, item.EmployeeId, request.ValidFrom, request.ValidTo, excludeId: id);

        bool changed = item.Price != request.Price || item.ValidFrom != request.ValidFrom || item.ValidTo != request.ValidTo;
        PriceListItemHistory history = changed
            ? new PriceListItemHistory
            {
                Id = Guid.NewGuid(),
                PriceListItemId = id,
                OldPrice = item.Price,
                NewPrice = request.Price,
                OldValidFrom = item.ValidFrom,
                NewValidFrom = request.ValidFrom,
                OldValidTo = item.ValidTo,
                NewValidTo = request.ValidTo,
                ChangedAt = _timeProvider.GetUtcNow(),
                ChangedBy = userId
            }
            : null;

        item.Price = request.Price;
        item.ValidFrom = request.ValidFrom;
        item.ValidTo = request.ValidTo;
        item.UpdatedAt = _timeProvider.GetUtcNow();
        item.UpdatedBy = userId;

        if (history != null)
            await _priceListItemHandler.UpdateWithHistory(item, history);
        else
            await _priceListItemHandler.Update(item);

        PriceListItemDto dto = await GetById(organizationId, id);
        dto.Warnings.AddRange(await SaveWarnings(organizationId, item));
        return dto;
    }

    /// <summary>T1-8 — upozorenja spremanja stavke (spremanje se nikad ne odbija zbog njih): (1) rupa između spremljene stavke i
    /// susjedne aktivne stavke ISTOG predmeta i konteksta (poslovnica, zaposlenik); (2) koliko zakazanih budućih sudjelovanja
    /// usluge u kontekstu stavke, s danom cjenika unutar važenja stavke, ZADRŽAVA spremljenu cijenu (primjena na zakazane = P5).
    /// Neaktivna stavka ne utječe na cijene, pa nema upozorenja.</summary>
    private async Task<List<WarningDto>> SaveWarnings(Guid organizationId, PriceListItem item)
    {
        List<WarningDto> warnings = new();
        if (!item.IsActive)
            return warnings;

        PricingSubjectType subjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package;
        Guid subjectId = item.ServiceId ?? item.PackageId!.Value;
        Guid itemId = item.Id.GetValueOrDefault();

        // Stavke istog opsega se ne preklapaju (EnsureNoOverlap), pa su susjedi uzastopne stavke po ValidFrom.
        List<PriceListItem> scope = (await _priceListItemHandler.GetActiveForExactScope(
                organizationId, subjectType, subjectId, item.CompanyId, item.EmployeeId, excludeId: null))
            .OrderBy(p => p.ValidFrom).ToList();
        for (int i = 1; i < scope.Count; i++)
        {
            PriceListItem before = scope[i - 1], after = scope[i];
            if (before.Id != itemId && after.Id != itemId)
                continue;
            if (before.ValidTo.HasValue && after.ValidFrom > before.ValidTo.Value.AddDays(1))
                warnings.Add(new WarningDto(WarningCodes.PriceListGap, new WarningPriceListGapDetails
                {
                    FromDate = before.ValidTo.Value.AddDays(1),
                    ToDate = after.ValidFrom.AddDays(-1)
                }));
        }

        if (item.ServiceId.HasValue)
        {
            int keep = await CountScheduledInValidity(organizationId, item);
            if (keep > 0)
                warnings.Add(new WarningDto(WarningCodes.PriceListScheduledKeepOldPrice, new WarningPriceListScheduledKeepDetails
                {
                    Count = keep,
                    ValidFrom = item.ValidFrom,
                    ValidTo = item.ValidTo
                }));
        }

        return warnings;
    }

    /// <summary>T1-8 — zakazana (Confirmed) sudjelovanja usluge stavke s početkom segmenta od sada, u poslovnici stavke (null =
    /// sve) i za zaposlenika stavke kao izvor cijene (null = bez filtra), čiji je dan cjenika (lokalni datum početka u zoni
    /// poslovnice termina, isto pravilo kao ResolveForServiceStart) unutar važenja stavke.</summary>
    private async Task<int> CountScheduledInValidity(Guid organizationId, PriceListItem item)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // Gruba UTC granica (±1 dan pokriva svaku zonu); točan dan se računa u zoni poslovnice niže.
        DateTimeOffset from = new DateTimeOffset(item.ValidFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        DateTimeOffset? to = item.ValidTo.HasValue
            ? new DateTimeOffset(item.ValidTo.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2)
            : null;
        List<(Guid CompanyId, DateTimeOffset PlannedStart)> starts = await _priceListItemHandler.GetScheduledServiceStarts(
            organizationId, item.ServiceId!.Value, item.CompanyId, item.EmployeeId, from > now ? from : now, to);
        if (starts.Count == 0)
            return 0;

        Dictionary<Guid, OrganizationCalendar> calendars = await _calendars.GetCompanyCalendars(
            organizationId, starts.Select(s => s.CompanyId).Distinct());
        return starts.Count(s =>
        {
            DateOnly day = calendars[s.CompanyId].LocalDate(s.PlannedStart);
            return day >= item.ValidFrom && (item.ValidTo == null || day <= item.ValidTo.Value);
        });
    }

    public async Task<PriceListItemDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        if (isActive && !item.IsActive)
        {
            PricingSubjectType subjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package;
            Guid subjectId = item.ServiceId ?? item.PackageId!.Value;
            await EnsureNoOverlap(organizationId, subjectType, subjectId, item.CompanyId, item.EmployeeId, item.ValidFrom, item.ValidTo, excludeId: id);
        }

        item.IsActive = isActive;
        item.UpdatedAt = _timeProvider.GetUtcNow();
        item.UpdatedBy = userId;

        await _priceListItemHandler.Update(item);
        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        PriceListItem item = await _priceListItemHandler.GetById(organizationId, id);
        if (item == null)
            throw new NotFoundAppException("PriceListItem", id);

        bool hasHistory = await _priceListItemHandler.HasHistory(id);
        if (hasHistory)
            throw new BusinessRuleException(ErrorCodes.ReferencedCannotDelete, "Stavka cjenika ima zabilježenu povijest promjena cijene i ne može se trajno obrisati — deaktivirajte je umjesto toga.");

        await _priceListItemHandler.Delete(item);
    }

    public async Task<List<EffectivePriceDto>> GetEffectivePriceList(Guid organizationId, Guid? companyId, DateOnly? requestedDate)
    {
        // T1-7: zadani dan = danas po poslovnom satu u zoni poslovnice (bez poslovnice zona organizacije).
        DateOnly date = requestedDate ?? await Today(organizationId, companyId);
        List<ServiceEntity> services = await _serviceHandler.GetAllActive(organizationId);
        List<Package> packages = await _packageHandler.GetAllActive(organizationId);
        List<PriceListItem> priceItems = await _priceListItemHandler.GetActiveForCompany(organizationId, companyId, date);

        List<EffectivePriceDto> result = new List<EffectivePriceDto>();

        foreach (ServiceEntity service in services)
        {
            List<PriceCandidate> candidates = priceItems
                .Where(p => p.ServiceId == service.Id)
                .Select(ToCandidate)
                .ToList();
            ResolvedPrice resolved = _priceResolutionService.Resolve(candidates, service.DefaultPrice, companyId, employeeId: null, date);

            result.Add(new EffectivePriceDto
            {
                SubjectType = PricingSubjectType.Service,
                SubjectId = service.Id.GetValueOrDefault(),
                SubjectName = service.Name,
                Price = resolved.Price,
                Source = resolved.Source
            });
        }

        foreach (Package package in packages)
        {
            List<PriceCandidate> candidates = priceItems
                .Where(p => p.PackageId == package.Id)
                .Select(ToCandidate)
                .ToList();
            ResolvedPrice resolved = _priceResolutionService.Resolve(candidates, package.DefaultPrice, companyId, employeeId: null, date);

            result.Add(new EffectivePriceDto
            {
                SubjectType = PricingSubjectType.Package,
                SubjectId = package.Id.GetValueOrDefault(),
                SubjectName = package.Name,
                Price = resolved.Price,
                Source = resolved.Source
            });
        }

        return result.OrderBy(r => r.SubjectType).ThenBy(r => r.SubjectName).ToList();
    }

    public async Task<ResolvePriceResponse> ResolvePrice(Guid organizationId, ResolvePriceRequest request)
    {
        DateOnly date = request.Date ?? await Today(organizationId, request.CompanyId);
        if (request.EmployeeId.HasValue && request.SubjectType != PricingSubjectType.Service)
            throw new ValidationAppException("Izvor cijene zaposlenika (EmployeeId) postoji samo za usluge.");
        (decimal defaultPrice, string subjectName) = await GetSubject(organizationId, request.SubjectType, request.SubjectId);

        List<PriceListItem> candidates = await _priceListItemHandler.GetActiveCandidates(
            organizationId, request.SubjectType, request.SubjectId, request.CompanyId, request.EmployeeId);

        ResolvedPrice resolved = _priceResolutionService.Resolve(
            candidates.Select(ToCandidate), defaultPrice, request.CompanyId, request.EmployeeId, date);

        // T1-8 (rupa u cjeniku): zadana cijena je i dalje valjan izvor, ali se označava kad subjekt ima stavke u kontekstu
        // razrješavanja (učitani kandidati), a nijedna ne pokriva dan, ili kad je zadana cijena 0 €.
        PriceNotDefinedReason? notDefined = resolved.Source != PriceSource.Default ? null
            : candidates.Count > 0 ? PriceNotDefinedReason.NoPriceListItemForDate
            : resolved.Price == 0m ? PriceNotDefinedReason.ZeroDefaultPrice
            : null;

        return new ResolvePriceResponse
        {
            SubjectType = request.SubjectType,
            SubjectId = request.SubjectId,
            CompanyId = request.CompanyId,
            EmployeeId = request.EmployeeId,
            Date = date,
            Price = resolved.Price,
            Source = resolved.Source,
            SubjectName = subjectName,
            PriceNotDefinedReason = notDefined
        };
    }

    public async Task<DateOnly> PriceListDay(Guid organizationId, Guid companyId, DateTimeOffset serviceStartsAt) =>
        (await _calendars.GetCompanyCalendar(organizationId, companyId)).LocalDate(serviceStartsAt);

    public async Task<ResolvePriceResponse> ResolveForServiceStart(
        Guid organizationId, Guid serviceId, Guid companyId, Guid? pricingEmployeeId, DateTimeOffset serviceStartsAt)
    {
        DateOnly serviceDate = await PriceListDay(organizationId, companyId, serviceStartsAt);
        return await ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Service,
            SubjectId = serviceId,
            CompanyId = companyId,
            EmployeeId = pricingEmployeeId,
            Date = serviceDate
        });
    }

    /// <summary>
    /// Dohvaća zadanu (default) cijenu subjekta. requireActive: true smije se koristiti SAMO pri kreiranju
    /// nove stavke cjenika (INACTIVE_SERVICE/INACTIVE_PACKAGE) — razrješavanje cijene (ResolvePrice) i pregled
    /// postojećih stavki moraju raditi i za već neaktivan subjekt (povijesni zapisi se ne smiju blokirati).
    /// </summary>
    private async Task<decimal> GetDefaultPrice(Guid organizationId, PricingSubjectType subjectType, Guid subjectId, bool requireActive = false) =>
        (await GetSubject(organizationId, subjectType, subjectId, requireActive)).DefaultPrice;

    /// <summary>Zadana cijena i naziv subjekta (vidi <see cref="GetDefaultPrice"/>).</summary>
    private async Task<(decimal DefaultPrice, string Name)> GetSubject(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, bool requireActive = false)
    {
        if (subjectType == PricingSubjectType.Service)
        {
            ServiceEntity service = await _serviceHandler.GetById(organizationId, subjectId);
            if (service == null)
                throw new NotFoundAppException("Service", subjectId);
            if (requireActive && !service.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveService, $"Usluga '{service.Name}' nije aktivna — nova stavka cjenika se ne može kreirati.");

            return (service.DefaultPrice, service.Name);
        }

        Package package = await _packageHandler.GetById(organizationId, subjectId);
        if (package == null)
            throw new NotFoundAppException("Package", subjectId);
        if (requireActive && !package.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactivePackage, $"Paket '{package.Name}' nije aktivan — nova stavka cjenika se ne može kreirati.");

        return (package.DefaultPrice, package.Name);
    }

    private async Task EnsureCompanyExists(Guid organizationId, Guid? companyId, bool requireActive = false)
    {
        if (!companyId.HasValue)
            return;

        Company company = await _companyHandler.GetById(organizationId, companyId.Value);
        if (company == null)
            throw new NotFoundAppException("Company", companyId.Value);
        if (requireActive && !company.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{company.Name}' nije aktivna — nova stavka cjenika se ne može kreirati za nju.");
    }

    /// <summary>Phase M1G: opseg stavke uključuje zaposlenika — dvije stavke se sudaraju samo kad adresiraju ISTI predmet,
    /// tvrtku I zaposlenika (null je zasebna vrijednost opsega).</summary>
    private async Task EnsureNoOverlap(
        Guid organizationId, PricingSubjectType subjectType, Guid subjectId, Guid? companyId, Guid? employeeId,
        DateOnly validFrom, DateOnly? validTo, Guid? excludeId)
    {
        List<PriceListItem> existing = await _priceListItemHandler.GetActiveForExactScope(
            organizationId, subjectType, subjectId, companyId, employeeId, excludeId);

        // T1-8: odbijanje navodi stavku s kojom se preklapa (najranija po ValidFrom) — details i datumi u poruci.
        PriceListItem conflicting = existing
            .Where(e => DateRangeOverlap.Overlaps(validFrom, validTo, e.ValidFrom, e.ValidTo))
            .OrderBy(e => e.ValidFrom).ThenBy(e => e.Id)
            .FirstOrDefault();
        if (conflicting != null)
        {
            string until = conflicting.ValidTo.HasValue ? conflicting.ValidTo.Value.ToString("dd.MM.yyyy.") : "bez kraja";
            throw new BusinessRuleException(ErrorCodes.PriceOverlap,
                $"Odabrano razdoblje se preklapa s postojećom aktivnom stavkom cjenika za istu kombinaciju usluge/paketa, tvrtke i " +
                $"zaposlenika (vrijedi od {conflicting.ValidFrom:dd.MM.yyyy.} do {until}).",
                new PriceOverlapDetails
                {
                    ConflictingItemId = conflicting.Id.GetValueOrDefault(),
                    ValidFrom = conflicting.ValidFrom,
                    ValidTo = conflicting.ValidTo,
                    SubjectType = subjectType,
                    ServiceId = subjectType == PricingSubjectType.Service ? subjectId : null,
                    PackageId = subjectType == PricingSubjectType.Package ? subjectId : null,
                    CompanyId = companyId,
                    EmployeeId = employeeId
                });
        }
    }

    /// <summary>Phase M1G: cijena zaposlenika postoji samo za uslugu; zaposlenik mora postojati u organizaciji (i biti aktivan
    /// za novu stavku — isto pravilo kao tvrtka).</summary>
    private async Task EnsurePriceEmployee(Guid organizationId, PricingSubjectType subjectType, Guid? employeeId, bool requireActive)
    {
        if (!employeeId.HasValue)
            return;
        if (subjectType != PricingSubjectType.Service)
            throw new ValidationAppException("Cijena zaposlenika (EmployeeId) smije se navesti samo za uslugu, ne za paket.");

        Employee employee = await _employeeHandler.GetById(organizationId, employeeId.Value);
        if (employee == null)
            throw new NotFoundAppException("Employee", employeeId.Value);
        if (requireActive && !employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, "Zaposlenik nije aktivan — nova stavka cjenika se ne može kreirati za njega.");
    }

    private static void ValidateDateRange(DateOnly validFrom, DateOnly? validTo)
    {
        if (validTo.HasValue && validTo.Value < validFrom)
            throw new ValidationAppException("Datum 'vrijedi do' ne smije biti prije datuma 'vrijedi od'.");
    }

    /// <summary>T1-7: današnji poslovni dan (poslovni sat) u zoni poslovnice; bez poslovnice zona organizacije.</summary>
    private async Task<DateOnly> Today(Guid organizationId, Guid? companyId) =>
        (await _calendars.GetCompanyOrOrganizationCalendar(organizationId, companyId)).LocalDate(_timeProvider.GetUtcNow());

    private static Guid ValidateSubject(PricingSubjectType subjectType, Guid? serviceId, Guid? packageId)
    {
        if (subjectType == PricingSubjectType.Service)
        {
            if (!serviceId.HasValue || packageId.HasValue)
                throw new ValidationAppException("Kad je SubjectType = Service, ServiceId je obavezan, a PackageId mora biti prazan.");

            return serviceId.Value;
        }

        if (!packageId.HasValue || serviceId.HasValue)
            throw new ValidationAppException("Kad je SubjectType = Package, PackageId je obavezan, a ServiceId mora biti prazan.");

        return packageId.Value;
    }

    private static PriceCandidate ToCandidate(PriceListItem item)
    {
        return new PriceCandidate
        {
            CompanyId = item.CompanyId,
            EmployeeId = item.EmployeeId,
            Price = item.Price,
            ValidFrom = item.ValidFrom,
            ValidTo = item.ValidTo,
            IsActive = item.IsActive
        };
    }

    private static PriceListItemDto ToDto(PriceListItem item)
    {
        return new PriceListItemDto
        {
            Id = item.Id.GetValueOrDefault(),
            SubjectType = item.ServiceId != null ? PricingSubjectType.Service : PricingSubjectType.Package,
            ServiceId = item.ServiceId,
            ServiceName = item.Service?.Name,
            PackageId = item.PackageId,
            PackageName = item.Package?.Name,
            CompanyId = item.CompanyId,
            CompanyName = item.Company?.Name,
            EmployeeId = item.EmployeeId,
            EmployeeName = item.Employee != null ? $"{item.Employee.FirstName} {item.Employee.LastName}" : null,
            Price = item.Price,
            ValidFrom = item.ValidFrom,
            ValidTo = item.ValidTo,
            IsActive = item.IsActive,
            CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy
        };
    }
}
