using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Organizations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using ProductEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Products.Product;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi ICommissionRuleService/ICommissionService/ICommissionLedgerService za domenske napomene. Jedna klasa
/// implementira sva tri sučelja — isti obrazac kao PaymentService (IPaymentService + IPaymentLedgerService).
///
/// GENERACIJSKA ODLUKA (Group Service): commission izvor je CIJELI odrađeni grupni termin (segment occurrencea), NE pojedina
/// odrađena prisutnost: trener grupne nastave je plaćen za ODRŽAVANJE termina (fiksno po terminu, = Vagaro Commission by Class).
/// Grupna provizija NEMA reverzijsku putanju (poništenje check-ina po sudioniku ne dira izvor).
///
/// INDIVIDUAL BOOKING REVERZIJA (ReverseForIndividualServiceCorrection): individualni completion IMA legitiman "undo"
/// (BookingService korekcija Completed -> Confirmed). Reverzija identificira izvor deterministički preko sudjelovanja i
/// SourceVersion (StatusVersion u trenutku zarade), NE po iznosu/datumu/zaposleniku.
///
/// P2 (2F, Vagaro model, ADR-0030): pravilo zaposlenika za uslugu ili opće pravilo zaposlenika (sve individualne usluge, razine);
/// pravilo za uslugu ima prednost i može biti "Bez provizije". Verzija se bira po datumu važenja, deaktivacija nikad ne vraća
/// stariju verziju. Osnovica za odrađeno: cijena sesije (ručni iznos ili cjenik), "oduzmi popuste" / "oduzmi popuste članstva"
/// (pokrivena sesija → 0); sesija pokrivena paketom = cijena sesije. Uz svaku proviziju objašnjenje izbora pravila. Provizija na
/// prodaju ide korisniku odabranom na stavci (članarina: na članstvu); naknadna dodjela kad korisnika nije bilo. Q38 provizija od
/// plaćene P1 naknade. Brojanje po događajima.
/// </summary>
public class CommissionService : ICommissionRuleService, ICommissionService, ICommissionLedgerService
{
    private const string SubjectWinsReason = "Pravilo za uslugu ima prednost pred općim pravilom zaposlenika.";

    private readonly ICommissionRuleHandler _ruleHandler;
    private readonly ICommissionEntryHandler _entryHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IProductHandler _productHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly IMembershipPlanHandler _membershipPlanHandler;
    private readonly IOrganizationSettingsHandler _settingsHandler;
    private readonly IOrganizationCalendarService _calendars;
    private readonly IAppointmentHandler _appointmentHandler;
    private readonly IClientMembershipHandler _membershipHandler;
    private readonly ICheckoutAuditLogHandler _checkoutAuditLogHandler;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public CommissionService(
        ICommissionRuleHandler ruleHandler,
        ICommissionEntryHandler entryHandler,
        IEmployeeHandler employeeHandler,
        IServiceHandler serviceHandler,
        IProductHandler productHandler,
        IPackageHandler packageHandler,
        IMembershipPlanHandler membershipPlanHandler,
        IOrganizationSettingsHandler settingsHandler,
        IOrganizationCalendarService calendars,
        IAppointmentHandler appointmentHandler,
        IClientMembershipHandler membershipHandler,
        ICheckoutAuditLogHandler checkoutAuditLogHandler,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _ruleHandler = ruleHandler;
        _entryHandler = entryHandler;
        _employeeHandler = employeeHandler;
        _serviceHandler = serviceHandler;
        _productHandler = productHandler;
        _packageHandler = packageHandler;
        _membershipPlanHandler = membershipPlanHandler;
        _settingsHandler = settingsHandler;
        _calendars = calendars;
        _appointmentHandler = appointmentHandler;
        _membershipHandler = membershipHandler;
        _checkoutAuditLogHandler = checkoutAuditLogHandler;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    #region Rule CRUD

    public async Task<List<CommissionRuleDto>> GetList(Guid organizationId, CommissionRuleQuery query)
    {
        List<CommissionRule> rules = await _ruleHandler.GetList(organizationId, query.EmployeeId, query.Kind, query.SubjectType, query.IsActive);
        return rules.Select(ToDto).ToList();
    }

    public async Task<CommissionRuleDto> GetById(Guid organizationId, Guid id)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        return ToDto(rule);
    }

    public async Task<CommissionRuleDto> Create(Guid organizationId, Guid userId, CommissionRuleCreateRequest request)
    {
        Employee employee = await _employeeHandler.GetById(organizationId, request.EmployeeId);
        if (employee == null)
            throw new NotFoundAppException("Employee", request.EmployeeId);

        CommissionRuleKind kind = KindFor(request.SubjectType, request.Kind);
        await ValidateSubjectAndPercentageRule(
            organizationId, request.SubjectType, request.ServiceId, request.ProductId, request.PackageId, request.MembershipPlanId, request.CalculationType);
        Guid ruleId = Guid.NewGuid();
        (CommissionCalculationType? calculationType, decimal? value, List<CommissionRuleTier> tiers) =
            ValidateAmounts(organizationId, ruleId, request.SubjectType, request.CalculationType, request.Value, request.Tiers);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CommissionRule rule = new CommissionRule
        {
            Id = ruleId,
            OrganizationId = organizationId,
            EmployeeId = request.EmployeeId,
            Kind = kind,
            SubjectType = request.SubjectType,
            ServiceId = request.ServiceId,
            ProductId = request.ProductId,
            PackageId = request.PackageId,
            MembershipPlanId = request.MembershipPlanId,
            EffectiveFrom = request.EffectiveFrom ?? DateOnly.MinValue,
            CalculationType = calculationType,
            Value = value,
            Tiers = tiers,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = userId
        };

        try
        {
            await _ruleHandler.Add(rule);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException(
                ErrorCodes.CommissionRuleAlreadyExists,
                "Zaposlenik već ima pravilo provizije za ovaj predmet s istim datumom važenja (i deaktivirana verzija zauzima datum).");
        }

        return await GetById(organizationId, ruleId);
    }

    public async Task<CommissionRuleDto> Update(Guid organizationId, Guid userId, Guid id, CommissionRuleUpdateRequest request)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        await ValidateSubjectAndPercentageRule(
            organizationId, rule.SubjectType, rule.ServiceId, rule.ProductId, rule.PackageId, rule.MembershipPlanId, request.CalculationType);
        (CommissionCalculationType? calculationType, decimal? value, List<CommissionRuleTier> tiers) =
            ValidateAmounts(organizationId, id, rule.SubjectType, request.CalculationType, request.Value, request.Tiers);

        rule.CalculationType = calculationType;
        rule.Value = value;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = userId;

        await _ruleHandler.Update(rule, tiers);

        return await GetById(organizationId, id);
    }

    /// <summary>P2 (2F, pregled — izbor 2): deaktivacija vrijedi od današnjeg datuma (kalendar organizacije) — od tada za tu verziju
    /// NEMA pravila i NIKAD se ne vraća starija verzija. Reaktivacija briše datum deaktivacije.</summary>
    public async Task<CommissionRuleDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        // Reaktivacija mora proći ISTU provjeru kao Create/Update — Service.ExecutionMode se mogao promijeniti
        // Individual->Group dok je pravilo bilo neaktivno (postotak za grupnu uslugu nije podržan).
        if (isActive)
            await ValidateSubjectAndPercentageRule(
                organizationId, rule.SubjectType, rule.ServiceId, rule.ProductId, rule.PackageId, rule.MembershipPlanId, rule.CalculationType);

        rule.IsActive = isActive;
        rule.DeactivatedFrom = isActive ? null : (await _calendars.GetCalendar(organizationId)).LocalDate(DateTimeOffset.UtcNow);
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = userId;

        await _ruleHandler.Update(rule);

        CommissionRuleDto result = await GetById(organizationId, id);
        // Potvrda 2026-10-08: deaktivirano pravilo za uslugu znači da od datuma deaktivacije vrijedi opće pravilo — upozorenje,
        // jer se za isključenje usluge bira "Bez provizije".
        if (!isActive && rule.SubjectType == CommissionSubjectType.Service)
        {
            DateOnly from = rule.DeactivatedFrom.GetValueOrDefault();
            CommissionRule general = await _ruleHandler.GetVersionOn(
                organizationId, rule.EmployeeId, CommissionRuleKind.Performance, CommissionSubjectType.AllServices, null, from);
            CommissionRuleTier tier = general != null && general.AppliesOn(from) ? general.Tiers.SingleOrDefault(t => t.FromRevenue == 0m) : null;
            if (tier != null)
                result.Warnings.Add(new WarningDto(WarningCodes.CommissionServiceRuleGeneralApplies, new WarningCommissionGeneralRuleAppliesDetails
                {
                    ServiceId = rule.ServiceId.GetValueOrDefault(),
                    EmployeeId = rule.EmployeeId,
                    EffectiveOn = from,
                    GeneralRuleId = general.Id.GetValueOrDefault(),
                    CalculationType = tier.CalculationType.ToString(),
                    Value = tier.Value
                }));
        }

        return result;
    }

    /// <summary>P2 (2F, pregled — izbor 2): verzija se smije obrisati samo ako po njoj nije nastala nijedna provizija (ispravak
    /// greške); tada za njezin raspon vrijedi prethodna verzija.</summary>
    public async Task Delete(Guid organizationId, Guid id)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        bool isReferenced = await _ruleHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(
                ErrorCodes.ReferencedCannotDelete,
                "Po ovoj verziji pravila su nastale provizije i ne može se obrisati — deaktivirajte je (od danas nema pravila).");

        await _ruleHandler.Delete(rule);
    }

    /// <summary>P2 (2F, §18.1) — vrsta slijedi predmet: usluga i opće pravilo = za odrađeno; proizvod/paket/plan = za prodaju.
    /// Provizija na prodaju usluge je zasebna odluka nakon 2F (Q52b).</summary>
    private static CommissionRuleKind KindFor(CommissionSubjectType subjectType, CommissionRuleKind? requested)
    {
        CommissionRuleKind kind = subjectType is CommissionSubjectType.Service or CommissionSubjectType.AllServices
            ? CommissionRuleKind.Performance
            : CommissionRuleKind.Sale;
        if (requested.HasValue && requested.Value != kind)
            throw new ValidationAppException(kind == CommissionRuleKind.Performance
                ? "Provizija na prodaju usluge još nije podržana (P2 Q52b) — pravilo za uslugu je uvijek za odrađeno (Performance)."
                : "Pravilo za proizvod, paket ili plan članarine je uvijek za prodaju (Sale).");
        return kind;
    }

    /// <summary>Provjerava da je točno jedan predmet popunjen prema SubjectType (AllServices: nijedan), da pripada ovoj organizaciji, i
    /// da Percentage nije zatražen za Service.ExecutionMode=Group.</summary>
    private async Task ValidateSubjectAndPercentageRule(
        Guid organizationId, CommissionSubjectType subjectType, Guid? serviceId, Guid? productId, Guid? packageId, Guid? membershipPlanId,
        CommissionCalculationType? calculationType)
    {
        int subjects = new[] { serviceId, productId, packageId, membershipPlanId }.Count(s => s.HasValue);
        switch (subjectType)
        {
            case CommissionSubjectType.AllServices:
                if (subjects != 0)
                    throw new ValidationAppException("Opće pravilo (AllServices) nema predmet.");
                return;

            case CommissionSubjectType.Service:
                if (!serviceId.HasValue || subjects != 1)
                    throw new ValidationAppException("Za SubjectType=Service mora biti popunjen isključivo ServiceId.");

                Service service = await _serviceHandler.GetById(organizationId, serviceId.Value);
                if (service == null)
                    throw new NotFoundAppException("Service", serviceId.Value);

                if (calculationType == CommissionCalculationType.Percentage && service.ExecutionMode == ServiceExecutionMode.Group)
                    throw new BusinessRuleException(
                        ErrorCodes.CommissionGroupPercentageNotSupported,
                        "Postotna provizija nije podržana za grupne usluge (nema nedvosmislene osnovice po terminu) — koristite Fixed.");
                return;

            case CommissionSubjectType.Product:
                if (!productId.HasValue || subjects != 1)
                    throw new ValidationAppException("Za SubjectType=Product mora biti popunjen isključivo ProductId.");

                ProductEntity product = await _productHandler.GetById(organizationId, productId.Value);
                if (product == null)
                    throw new NotFoundAppException("Product", productId.Value);
                return;

            case CommissionSubjectType.Package:
                if (!packageId.HasValue || subjects != 1)
                    throw new ValidationAppException("Za SubjectType=Package mora biti popunjen isključivo PackageId.");

                Package package = await _packageHandler.GetById(organizationId, packageId.Value);
                if (package == null)
                    throw new NotFoundAppException("Package", packageId.Value);
                return;

            case CommissionSubjectType.MembershipPlan:
                if (!membershipPlanId.HasValue || subjects != 1)
                    throw new ValidationAppException("Za SubjectType=MembershipPlan mora biti popunjen isključivo MembershipPlanId.");

                MembershipPlan plan = await _membershipPlanHandler.GetById(organizationId, membershipPlanId.Value);
                if (plan == null)
                    throw new NotFoundAppException("MembershipPlan", membershipPlanId.Value);
                return;

            default:
                throw new ValidationAppException("Nepoznat SubjectType.");
        }
    }

    /// <summary>Pravilo predmeta: izračun + vrijednost (None = "Bez provizije", samo za uslugu). Opće pravilo: točno jedna razina bez
    /// praga (P2; faza Payroll dodaje razine po prometu), Percentage | Fixed.</summary>
    private static (CommissionCalculationType?, decimal?, List<CommissionRuleTier>) ValidateAmounts(
        Guid organizationId, Guid ruleId, CommissionSubjectType subjectType, CommissionCalculationType? calculationType, decimal? value,
        List<CommissionRuleTierDto> tiers)
    {
        if (subjectType == CommissionSubjectType.AllServices)
        {
            if (calculationType.HasValue || value.HasValue)
                throw new ValidationAppException("Opće pravilo zadaje iznos kroz razinu (Tiers), ne kroz CalculationType/Value.");
            if (tiers == null || tiers.Count != 1 || tiers[0].FromRevenue != 0m)
                throw new ValidationAppException("Opće pravilo ima točno jednu razinu bez praga (FromRevenue = 0); razine po prometu dolaze s obračunom provizija.");
            CommissionRuleTierDto tier = tiers[0];
            if (tier.CalculationType == CommissionCalculationType.None)
                throw new ValidationAppException("Razina općeg pravila je postotak ili fiksni iznos; \"Bez provizije\" se zadaje pravilom za uslugu.");
            ValidateValue(tier.CalculationType, tier.Value);
            return (null, null, new List<CommissionRuleTier>
            {
                new() { Id = Guid.NewGuid(), OrganizationId = organizationId, CommissionRuleId = ruleId, FromRevenue = 0m,
                    CalculationType = tier.CalculationType, Value = tier.Value }
            });
        }

        if (tiers is { Count: > 0 })
            throw new ValidationAppException("Razine postoje samo za opće pravilo (AllServices).");
        CommissionCalculationType type = calculationType ?? throw new ValidationAppException("CalculationType je obavezan.");
        if (type == CommissionCalculationType.None)
        {
            if (subjectType != CommissionSubjectType.Service)
                throw new ValidationAppException("\"Bez provizije\" se zadaje samo za uslugu (izričito isključuje uslugu iz općeg pravila).");
            return (type, 0m, new List<CommissionRuleTier>());
        }

        decimal amount = value ?? throw new ValidationAppException("Value je obavezan.");
        ValidateValue(type, amount);
        return (type, amount, new List<CommissionRuleTier>());
    }

    private static void ValidateValue(CommissionCalculationType calculationType, decimal value)
    {
        if (calculationType == CommissionCalculationType.Percentage)
        {
            if (value < 0m || value > 100m)
                throw new ValidationAppException("Postotna vrijednost mora biti između 0 i 100.");
        }
        else if (value < 0m)
        {
            throw new ValidationAppException("Fiksna vrijednost mora biti veća ili jednaka 0.");
        }
    }

    private static CommissionRuleDto ToDto(CommissionRule rule)
    {
        return new CommissionRuleDto
        {
            Id = rule.Id.GetValueOrDefault(),
            EmployeeId = rule.EmployeeId,
            EmployeeName = rule.Employee != null ? $"{rule.Employee.FirstName} {rule.Employee.LastName}" : null,
            Kind = rule.Kind,
            SubjectType = rule.SubjectType,
            ServiceId = rule.ServiceId,
            ServiceName = rule.Service?.Name,
            ProductId = rule.ProductId,
            ProductName = rule.Product?.Name,
            PackageId = rule.PackageId,
            PackageName = rule.Package?.Name,
            MembershipPlanId = rule.MembershipPlanId,
            MembershipPlanName = rule.MembershipPlan?.Name,
            EffectiveFrom = EffectiveFromOf(rule),
            DeactivatedFrom = rule.DeactivatedFrom,
            CalculationType = rule.CalculationType,
            Value = rule.Value,
            Tiers = rule.Tiers
                .OrderBy(t => t.FromRevenue)
                .Select(t => new CommissionRuleTierDto { FromRevenue = t.FromRevenue, CalculationType = t.CalculationType, Value = t.Value })
                .ToList(),
            IsActive = rule.IsActive,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    private static DateOnly? EffectiveFromOf(CommissionRule rule) => rule.EffectiveFrom == DateOnly.MinValue ? null : rule.EffectiveFrom;

    #endregion

    #region Queries

    public async Task<PagedResult<CommissionEntryDto>> GetEntries(Guid organizationId, CommissionEntryQuery query)
    {
        if (query.To < query.From)
            throw new ValidationAppException("'To' ne smije biti prije 'From'.");

        (List<CommissionEntry> items, int totalCount) = await _entryHandler.GetPaged(organizationId, query);

        List<CommissionEntryDto> dtos = items.Select(e => ToDto(e, query.From, query.To)).ToList();
        return PagedResult<CommissionEntryDto>.Create(dtos, totalCount, query.Page, query.PageSize);
    }

    public async Task<CommissionSummaryResultDto> GetSummary(Guid organizationId, CommissionSummaryQuery query)
    {
        if (query.To < query.From)
            throw new ValidationAppException("'To' ne smije biti prije 'From'.");

        List<EmployeeCommissionSummaryDto> employees = await _entryHandler.GetSummaryByEmployee(organizationId, query);

        return new CommissionSummaryResultDto
        {
            Employees = employees,
            TotalNetAmount = employees.Sum(e => e.NetAmount)
        };
    }

    private static CommissionEntryDto ToDto(CommissionEntry entry, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        bool InPeriod(DateTimeOffset? at) => at.HasValue && (!from.HasValue || at >= from) && (!to.HasValue || at < to);
        return new CommissionEntryDto
        {
            Id = entry.Id.GetValueOrDefault(),
            EmployeeId = entry.EmployeeId,
            EmployeeName = entry.Employee != null ? $"{entry.Employee.FirstName} {entry.Employee.LastName}" : null,
            CompanyId = entry.CompanyId,
            CompanyName = entry.Company?.Name,
            SourceType = entry.SourceType,
            AppointmentId = entry.AppointmentId,
            BookingId = entry.BookingId,
            BookingSegmentParticipationId = entry.BookingSegmentParticipationId,
            AppointmentSegmentId = entry.AppointmentSegmentId,
            CheckoutItemId = entry.CheckoutItemId,
            ClientMembershipId = entry.ClientMembershipId,
            ParticipationPolicyConsequenceId = entry.ParticipationPolicyConsequenceId,
            BaseAmount = entry.BaseAmount,
            CalculationType = entry.CalculationType,
            RuleValue = entry.RuleValue,
            CommissionAmount = entry.CommissionAmount,
            PaymentSource = entry.PaymentSource,
            CoverageSourceId = entry.CoverageSourceId,
            SessionPriceAmount = entry.SessionPriceAmount,
            ListPriceAmount = entry.ListPriceAmount,
            IsManualPrice = entry.IsManualPrice,
            DeductDiscounts = entry.DeductDiscounts,
            DeductMembershipDiscounts = entry.DeductMembershipDiscounts,
            AppliedRuleScope = entry.AppliedRuleScope,
            RuleEvaluation = entry.RuleEvaluation == null ? null : JsonSerializer.Deserialize<CommissionRuleEvaluationDto>(entry.RuleEvaluation),
            WasCapped = entry.WasCapped,
            Status = entry.Status,
            EarnedAt = entry.EarnedAt,
            ReversedAt = entry.ReversedAt,
            ReversalReason = entry.ReversalReason,
            CorrectionOfEntryId = entry.CorrectionOfEntryId,
            PeriodAmount = (InPeriod(entry.EarnedAt) ? entry.CommissionAmount : 0m) - (InPeriod(entry.ReversedAt) ? entry.CommissionAmount : 0m)
        };
    }

    #endregion

    #region Korisnik provizije na prodaju: korekcija (Q50) i naknadna dodjela

    public async Task<CommissionEntryReassignResultDto> Reassign(
        Guid organizationId, Guid userId, Guid entryId, CommissionEntryReassignRequest request)
    {
        Guid newEmployeeId = request?.EmployeeId ?? throw new ValidationAppException("EmployeeId je obavezan.");
        string reason = RequiredReason(request.Reason, "Razlog korekcije korisnika provizije je obavezan.");
        await EnsureSelectableEmployee(organizationId, newEmployeeId);

        Guid? createdId = null;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            CommissionEntry entry = await _entryHandler.GetForUpdate(uow, organizationId, entryId)
                ?? throw new NotFoundAppException("CommissionEntry", entryId);
            if (entry.Status != CommissionEntryStatus.Earned || !IsSaleSource(entry.SourceType))
                throw new BusinessRuleException(ErrorCodes.CommissionEntryNotReassignable,
                    "Korisnik se može ispraviti samo za aktivnu (zarađenu) proviziju na prodaju.");
            if (entry.EmployeeId == newEmployeeId)
                throw new ValidationAppException("Provizija već pripada tom zaposleniku.");

            // 2F-11: PRIJE ikakve promjene — ako novi korisnik nema pravilo važeće na datum izvorne provizije, nova provizija ne bi
            // nastala; bez izričite potvrde naredba ne mijenja ništa i vraća upozorenje (frontend ga prikaže i ponovi s potvrdom).
            DateOnly ruleDate = (await _calendars.GetCompanyCalendar(organizationId, entry.CompanyId)).LocalDate(entry.EarnedAt);
            (CommissionSubjectType checkType, Guid checkId) = await SaleSubjectOf(uow, organizationId, entry);
            CommissionRule newRule = await _ruleHandler.GetVersionOn(organizationId, newEmployeeId, CommissionRuleKind.Sale, checkType, checkId, ruleDate);
            if ((newRule == null || !newRule.AppliesOn(ruleDate)) && !request.ConfirmWithoutCommission)
                throw new BusinessRuleException(ErrorCodes.CommissionReassignWithoutRule,
                    "Odabrani zaposlenik nema pravilo provizije za ovu prodaju: postojeća provizija bi se stornirala, a nova ne bi nastala. Potvrdite ako je to namjera.",
                    new { employeeId = newEmployeeId, ruleDate, subjectType = checkType.ToString(), subjectId = checkId });

            (CommissionSubjectType subjectType, Guid subjectId) = await SetSaleRecipient(
                uow, organizationId, userId, entry.SourceType, SourceIdOf(entry), newEmployeeId, reason, "SaleCommissionCorrected");
            List<CommissionEntry> history = await _entryHandler.GetForSource(uow, organizationId, entry.SourceType, SourceIdOf(entry));
            await Reverse(uow, userId, entry, $"Korekcija korisnika provizije: {reason}");

            // Pravilo novog korisnika važeće na datum nastanka izvorne provizije (ista osnovica, isti trenutak prodaje).
            DateOnly earnedOn = (await _calendars.GetCompanyCalendar(organizationId, entry.CompanyId)).LocalDate(entry.EarnedAt);
            CommissionEntry created = await TryEarnSale(uow, organizationId, newEmployeeId, entry.SourceType, subjectType, subjectId,
                entry.CheckoutItemId, entry.ClientMembershipId, entry.CompanyId, entry.BaseAmount, earnedOn,
                history.Max(e => e.SourceVersion) + 1, entry.Id);
            createdId = created?.Id;

            await uow.CommitAsync();
        }

        return new CommissionEntryReassignResultDto
        {
            Reversed = ToDto(await _entryHandler.GetById(organizationId, entryId)),
            Created = createdId.HasValue ? ToDto(await _entryHandler.GetById(organizationId, createdId.Value)) : null
        };
    }

    public async Task<CommissionSaleAssignmentResultDto> AssignSale(Guid organizationId, Guid userId, CommissionSaleAssignmentRequest request)
    {
        if (request == null || request.CheckoutItemId.HasValue == request.ClientMembershipId.HasValue)
            throw new ValidationAppException("Zadajte točno jedan izvor: CheckoutItemId ili ClientMembershipId.");
        Guid employeeId = request.EmployeeId ?? throw new ValidationAppException("EmployeeId je obavezan.");
        string reason = RequiredReason(request.Reason, "Razlog naknadne dodjele korisnika provizije je obavezan.");
        await EnsureSelectableEmployee(organizationId, employeeId);

        Guid? createdId = null;
        await using (IUnitOfWork uow = await _unitOfWorkFactory.Begin())
        {
            createdId = request.ClientMembershipId.HasValue
                ? await AssignMembershipSale(uow, organizationId, userId, request.ClientMembershipId.Value, employeeId, reason)
                : await AssignItemSale(uow, organizationId, userId, request.CheckoutItemId.Value, employeeId, reason);
            await uow.CommitAsync();
        }

        return new CommissionSaleAssignmentResultDto
        {
            Created = createdId.HasValue ? ToDto(await _entryHandler.GetById(organizationId, createdId.Value)) : null
        };
    }

    private async Task<Guid?> AssignMembershipSale(IUnitOfWork uow, Guid organizationId, Guid userId, Guid membershipId, Guid employeeId, string reason)
    {
        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, membershipId)
            ?? throw new NotFoundAppException("ClientMembership", membershipId);
        if (membership.FirstSaleCommissionOutcome != MembershipFirstSaleCommissionOutcome.NoRecipient)
            throw new BusinessRuleException(ErrorCodes.CommissionSaleNotAssignable,
                "Naknadna dodjela je moguća samo kad provizija na prvu prodaju nije nastala jer korisnika nije bilo.");

        await SetSaleRecipient(uow, organizationId, userId, CommissionSourceType.MembershipSale, membershipId, employeeId, reason, "SaleCommissionAssigned");
        DateOnly earnedOn = (await _calendars.GetCalendar(organizationId)).LocalDate(membership.FirstSaleSettledAt.GetValueOrDefault());
        CommissionEntry created = await TryEarnSale(uow, organizationId, employeeId, CommissionSourceType.MembershipSale,
            CommissionSubjectType.MembershipPlan, membership.MembershipPlanId, null, membershipId, membership.SoldCompanyId,
            membership.FirstSaleBaseAmount.GetValueOrDefault(), earnedOn, await NextSourceVersion(uow, organizationId, CommissionSourceType.MembershipSale, membershipId), null);
        membership.FirstSaleCommissionOutcome = created != null ? MembershipFirstSaleCommissionOutcome.Earned : MembershipFirstSaleCommissionOutcome.NoRule;
        await uow.Context.SaveChangesAsync();
        return created?.Id;
    }

    private async Task<Guid?> AssignItemSale(IUnitOfWork uow, Guid organizationId, Guid userId, Guid itemId, Guid employeeId, string reason)
    {
        CheckoutItem item = await uow.Context.CheckoutItems
            .Include(i => i.Checkout)
            .SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.Id == itemId)
            ?? throw new NotFoundAppException("CheckoutItem", itemId);
        CommissionSourceType sourceType = item.Type == CheckoutItemType.Product ? CommissionSourceType.ProductSale : CommissionSourceType.PackageSale;
        bool assignable = item.Type is CheckoutItemType.Product or CheckoutItemType.Package
                          && item.Checkout.Status == CheckoutStatus.Completed
                          && item.SaleCommissionEmployeeId == null
                          && (await _entryHandler.GetForSource(uow, organizationId, sourceType, itemId)).Count == 0;
        if (!assignable)
            throw new BusinessRuleException(ErrorCodes.CommissionSaleNotAssignable,
                "Naknadna dodjela je moguća samo za proizvod ili paket zatvorenog checkouta čija provizija nije nastala jer korisnika nije bilo.");

        (CommissionSubjectType subjectType, Guid subjectId) = await SetSaleRecipient(
            uow, organizationId, userId, sourceType, itemId, employeeId, reason, "SaleCommissionAssigned");
        DateOnly earnedOn = (await _calendars.GetCompanyCalendar(organizationId, item.Checkout.CompanyId))
            .LocalDate(item.Checkout.CompletedAt.GetValueOrDefault());
        CommissionEntry created = await TryEarnSale(uow, organizationId, employeeId, sourceType, subjectType, subjectId,
            itemId, null, item.Checkout.CompanyId, item.Amount, earnedOn, 0, null);
        return created?.Id;
    }

    private static string RequiredReason(string reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException(message);
        string trimmed = reason.Trim();
        if (trimmed.Length > 500)
            throw new ValidationAppException("Razlog može imati najviše 500 znakova.");
        return trimmed;
    }

    /// <summary>P2 (2F) — korisnik provizije na prodaju mora biti postojeći AKTIVAN zaposlenik u trenutku odabira (stavka,
    /// članstvo, korekcija, naknadna dodjela). Kasnija neaktivnost ne poništava odabir: provizija nastaje njemu.</summary>
    public async Task EnsureSelectableEmployee(Guid organizationId, Guid employeeId)
    {
        Employee employee = await _employeeHandler.GetByIdLight(organizationId, employeeId)
            ?? throw new NotFoundAppException("Employee", employeeId);
        if (!employee.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveEmployee, "Neaktivan zaposlenik ne može biti korisnik provizije.");
    }

    private static bool IsSaleSource(CommissionSourceType sourceType) =>
        sourceType is CommissionSourceType.ProductSale or CommissionSourceType.PackageSale or CommissionSourceType.MembershipSale;

    private static Guid SourceIdOf(CommissionEntry entry) => entry.SourceType switch
    {
        CommissionSourceType.MembershipSale => entry.ClientMembershipId.GetValueOrDefault(),
        CommissionSourceType.PolicyFee => entry.BookingSegmentParticipationId.GetValueOrDefault(),
        _ => entry.CheckoutItemId.GetValueOrDefault()
    };

    /// <summary>Predmet pravila za prodaju izvora provizije (bez promjena): plan članarine ili proizvod/paket stavke.</summary>
    private static async Task<(CommissionSubjectType, Guid)> SaleSubjectOf(IUnitOfWork uow, Guid organizationId, CommissionEntry entry)
    {
        if (entry.SourceType == CommissionSourceType.MembershipSale)
            return (CommissionSubjectType.MembershipPlan, await uow.Context.ClientMemberships
                .Where(m => m.OrganizationId == organizationId && m.Id == entry.ClientMembershipId)
                .Select(m => m.MembershipPlanId)
                .SingleAsync());

        CheckoutItem item = await uow.Context.CheckoutItems.AsNoTracking()
            .SingleAsync(i => i.OrganizationId == organizationId && i.Id == entry.CheckoutItemId);
        return item.Type == CheckoutItemType.Product
            ? (CommissionSubjectType.Product, item.ProductId.GetValueOrDefault())
            : (CommissionSubjectType.Package, item.PackageId.GetValueOrDefault());
    }

    private async Task<int> NextSourceVersion(IUnitOfWork uow, Guid organizationId, CommissionSourceType sourceType, Guid sourceId)
    {
        List<CommissionEntry> history = await _entryHandler.GetForSource(uow, organizationId, sourceType, sourceId);
        return history.Count == 0 ? 0 : history.Max(e => e.SourceVersion) + 1;
    }

    /// <summary>Izvor korisnika provizije dobiva novog korisnika i zapis promjene: članstvo (povijest članstva) ili stavka checkouta
    /// (CheckoutAuditLog). Vraća predmet pravila za prodaju.</summary>
    private async Task<(CommissionSubjectType, Guid)> SetSaleRecipient(
        IUnitOfWork uow, Guid organizationId, Guid userId, CommissionSourceType sourceType, Guid sourceId, Guid newEmployeeId,
        string reason, string changeType)
    {
        if (sourceType == CommissionSourceType.MembershipSale)
        {
            ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, sourceId);
            Guid? old = membership.SaleCommissionEmployeeId;
            membership.SaleCommissionEmployeeId = newEmployeeId;
            membership.UpdatedAt = DateTimeOffset.UtcNow;
            membership.UpdatedBy = userId;
            _membershipHandler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, changeType, old?.ToString(), newEmployeeId.ToString(), reason));
            await uow.Context.SaveChangesAsync();
            return (CommissionSubjectType.MembershipPlan, membership.MembershipPlanId);
        }

        CheckoutItem item = await uow.Context.CheckoutItems.SingleAsync(i => i.OrganizationId == organizationId && i.Id == sourceId);
        Guid? previous = item.SaleCommissionEmployeeId;
        item.SaleCommissionEmployeeId = newEmployeeId;
        await uow.Context.SaveChangesAsync();
        await _checkoutAuditLogHandler.Add(uow, new CheckoutAuditLog
        {
            Id = Guid.NewGuid(),
            CheckoutId = item.CheckoutId,
            ChangeType = changeType,
            OldValue = $"{item.Id}:{previous}",
            NewValue = $"{item.Id}:{newEmployeeId}:{reason}",
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = userId
        });
        return item.Type == CheckoutItemType.Product
            ? (CommissionSubjectType.Product, item.ProductId.GetValueOrDefault())
            : (CommissionSubjectType.Package, item.PackageId.GetValueOrDefault());
    }

    /// <summary>Provizija na prodaju po pravilu korisnika važećem na zadani datum; null kad pravila nema (konačno).</summary>
    private async Task<CommissionEntry> TryEarnSale(
        IUnitOfWork uow, Guid organizationId, Guid employeeId, CommissionSourceType sourceType, CommissionSubjectType subjectType, Guid subjectId,
        Guid? checkoutItemId, Guid? membershipId, Guid companyId, decimal baseAmount, DateOnly ruleDate, int sourceVersion, Guid? correctionOf)
    {
        CommissionRule rule = await _ruleHandler.GetVersionOn(organizationId, employeeId, CommissionRuleKind.Sale, subjectType, subjectId, ruleDate);
        if (rule == null || !rule.AppliesOn(ruleDate))
            return null;

        RuleChoice choice = RuleChoice.ForSubject(rule, null, "Pravilo za prodaju predmeta.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CommissionEntry entry = new CommissionEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = employeeId,
            CompanyId = companyId,
            CommissionRuleId = rule.Id.GetValueOrDefault(),
            SourceType = sourceType,
            CheckoutItemId = checkoutItemId,
            ClientMembershipId = membershipId,
            BaseAmount = baseAmount,
            CalculationType = choice.CalculationType,
            RuleValue = choice.Value,
            CommissionAmount = Calculate(choice.CalculationType, choice.Value, baseAmount),
            AppliedRuleScope = choice.Scope,
            RuleEvaluation = choice.EvaluationJson,
            Status = CommissionEntryStatus.Earned,
            SourceVersion = sourceVersion,
            CorrectionOfEntryId = correctionOf,
            EarnedAt = now,
            CreatedAt = now
        };
        await TryAdd(uow, entry);
        return entry;
    }

    #endregion

    #region Ledger generation (called from AppointmentService/BookingService/CheckoutService/ClientMembershipService within their own transaction)

    public async Task GenerateForIndividualServiceCompletion(
        IUnitOfWork uow, Guid organizationId, ParticipationExecutionContext execution, BookingSegmentParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(participation);

        // Phase M1G: SVAKI zaposlenik segmenta zarađuje NEOVISNO prema SVOM pravilu (bez dijeljenja, bez "glavnog" zaposlenika;
        // izvor cijene NIJE korisnik provizije). P2 (2F, Vagaro): pravilo za uslugu ili opće pravilo važeće na datum sesije.
        CommissionBaseSettings settings = await LoadSettings(organizationId);
        DateOnly sessionDate = await SessionDate(organizationId, execution);
        (CommissionPaymentSource source, Guid? coverageSourceId) = await ResolvePaymentSource(uow, organizationId, participation);

        // Osnovica (Vagaro): cijena sesije = ručni iznos ako je upisan (nije popust), inače cjenik. "Oduzmi popuste" i "oduzmi
        // popuste članstva" uzimaju cijenu nakon odgovarajuće prilagodbe; sesija koju članarina pokriva u cijelosti uz "oduzmi
        // popuste članstva" ima osnovicu 0. Paket je način plaćanja: osnovica ostaje cijena sesije.
        bool manual = participation.IsAmountManuallyOverridden;
        decimal sessionPrice = manual ? participation.Amount : participation.BaseAmount ?? participation.Amount;
        decimal baseAmount = sessionPrice;
        if (!manual && participation.AdjustmentType.HasValue)
        {
            bool deduct = participation.AdjustmentType == PriceAdjustmentType.Membership ? settings.DeductMembershipDiscounts : settings.DeductDiscounts;
            if (deduct)
                baseAmount = participation.Amount;
        }
        if (source == CommissionPaymentSource.Membership && settings.DeductMembershipDiscounts)
            baseAmount = 0m;
        baseAmount = Math.Max(baseAmount, 0m);

        foreach (Guid employeeId in execution.EmployeeIds.Distinct().OrderBy(id => id))
        {
            RuleChoice choice = await ChooseServiceRule(organizationId, employeeId, execution.ServiceId, sessionDate, includeGeneral: true);
            if (choice == null)
                continue;

            await TryAdd(uow, new CommissionEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                CompanyId = execution.CompanyId,
                CommissionRuleId = choice.RuleId,
                SourceType = CommissionSourceType.IndividualService,
                AppointmentId = execution.AppointmentId,
                BookingId = participation.BookingId,
                BookingSegmentParticipationId = participation.Id,
                BaseAmount = baseAmount,
                CalculationType = choice.CalculationType,
                RuleValue = choice.Value,
                CommissionAmount = Calculate(choice.CalculationType, choice.Value, baseAmount),
                PaymentSource = source,
                CoverageSourceId = coverageSourceId,
                SessionPriceAmount = sessionPrice,
                ListPriceAmount = participation.BaseAmount,
                IsManualPrice = manual,
                DeductDiscounts = settings.DeductDiscounts,
                DeductMembershipDiscounts = settings.DeductMembershipDiscounts,
                AppliedRuleScope = choice.Scope,
                RuleEvaluation = choice.EvaluationJson,
                Status = CommissionEntryStatus.Earned,
                // StatusVersion IZVORNOG SUDJELOVANJA NAKON prijelaza u Completed — zaseban identitet ove completion-pojave.
                SourceVersion = participation.StatusVersion,
                EarnedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    public async Task<List<WarningDto>> GenerateForGroupServiceCompletion(IUnitOfWork uow, Guid organizationId, SegmentExecutionContext execution)
    {
        List<WarningDto> unsupported = new();
        DateOnly sessionDate = await SessionDate(organizationId, execution);

        // Phase M1G: izvor je SEGMENT occurrencea; svaki zaposlenik segmenta dobiva Fixed proviziju sesije JEDNOM (broj
        // klijenata je ne množi). Segment bez zaposlenika nema korisnika provizije. P2 (2F): samo pravilo usluge (opće pravilo vrijedi
        // za individualne usluge), po datumu važenja; "Bez provizije" = ništa.
        foreach (Guid employeeId in execution.EmployeeIds.Distinct().OrderBy(id => id))
        {
            RuleChoice choice = await ChooseServiceRule(organizationId, employeeId, execution.ServiceId, sessionDate, includeGeneral: false);
            if (choice == null)
                continue;

            // Grupna sesija nema osnovicu za postotak — pravilo se ne evaluira i ne tretira tiho kao Fixed: eksplicitno upozorenje
            // (pokriva promjenu načina izvođenja usluge nakon stvaranja pravila).
            if (choice.CalculationType != CommissionCalculationType.Fixed)
            {
                unsupported.Add(new WarningDto(WarningCodes.GroupCommissionRuleNotSupported, new WarningGroupCommissionRuleDetails
                {
                    SegmentId = execution.SegmentId,
                    EmployeeId = employeeId,
                    CommissionRuleId = choice.RuleId,
                    CalculationType = choice.CalculationType.ToString()
                }));
                continue;
            }

            await TryAdd(uow, new CommissionEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                CompanyId = execution.CompanyId,
                CommissionRuleId = choice.RuleId,
                SourceType = CommissionSourceType.GroupService,
                AppointmentId = execution.AppointmentId,
                AppointmentSegmentId = execution.SegmentId,
                BookingId = null,
                BaseAmount = 0m,
                CalculationType = choice.CalculationType,
                RuleValue = choice.Value,
                CommissionAmount = choice.Value,
                AppliedRuleScope = choice.Scope,
                RuleEvaluation = choice.EvaluationJson,
                Status = CommissionEntryStatus.Earned,
                EarnedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return unsupported;
    }

    public async Task GenerateForCheckoutCompletion(IUnitOfWork uow, Guid organizationId, Guid completedByUserId, Checkout checkout)
    {
        DateOnly saleDate = (await _calendars.GetCompanyCalendar(organizationId, checkout.CompanyId)).LocalDate(DateTimeOffset.UtcNow);

        // §18.1 ("Sold By"): korisnik je zaposlenik ODABRAN na stavci (default onaj koji je stavku dodao), ne onaj koji zatvara
        // checkout. Odabran je dok je bio aktivan, pa provizija nastaje i ako je u međuvremenu postao neaktivan. Bez korisnika →
        // nema provizije (naknadna dodjela uz commissions.manage); bez pravila → konačno.
        foreach (CheckoutItem item in checkout.Items.Where(i => i.SaleCommissionEmployeeId.HasValue))
        {
            (CommissionSubjectType subjectType, CommissionSourceType sourceType, Guid subjectId) = item.Type switch
            {
                CheckoutItemType.Product => (CommissionSubjectType.Product, CommissionSourceType.ProductSale, item.ProductId.GetValueOrDefault()),
                CheckoutItemType.Package => (CommissionSubjectType.Package, CommissionSourceType.PackageSale, item.PackageId.GetValueOrDefault()),
                // Booking i zaduženje obnove: odabir se samo sprema (Q52); prva prodaja članarine niže.
                _ => (default(CommissionSubjectType), default(CommissionSourceType), Guid.Empty)
            };
            if (subjectId == Guid.Empty)
                continue;

            await TryEarnSale(uow, organizationId, item.SaleCommissionEmployeeId.Value, sourceType, subjectType, subjectId,
                item.Id, null, checkout.CompanyId, item.Amount, saleDate, 0, null);
        }

        // Q42: provjera "zaduženja prve prodaje konačna" pri svakom Complete checkouta koji plaća zaduženje članarine.
        List<Guid> chargeIds = checkout.Items.Where(i => i.MembershipChargeId.HasValue).Select(i => i.MembershipChargeId.Value).ToList();
        if (chargeIds.Count == 0)
            return;
        List<Guid> membershipIds = await uow.Context.MembershipCharges
            .Where(c => c.OrganizationId == organizationId && chargeIds.Contains(c.Id))
            .Select(c => c.ClientMembershipId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync();
        foreach (Guid membershipId in membershipIds)
            await EvaluateMembershipFirstSale(uow, organizationId, completedByUserId, membershipId);
    }

    public async Task EvaluateMembershipFirstSale(IUnitOfWork uow, Guid organizationId, Guid userId, Guid membershipId)
    {
        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, membershipId);
        if (membership == null || membership.FirstSaleSettledAt != null || membership.VoidedAt != null)
            return;

        List<MembershipCharge> charges = MembershipFirstSale.ChargesOf(membership);
        if (!MembershipFirstSale.IsSettled(charges))
            return;

        // Q42 — evaluira se jednom, prvi put kad je uvjet ispunjen; ishod se pamti (NoRecipient dopušta naknadnu dodjelu).
        DateTimeOffset now = DateTimeOffset.UtcNow;
        decimal baseAmount = MembershipFirstSale.PaidAmount(charges);
        membership.FirstSaleSettledAt = now;
        membership.FirstSaleBaseAmount = baseAmount;
        if (baseAmount <= 0m)
            membership.FirstSaleCommissionOutcome = MembershipFirstSaleCommissionOutcome.ZeroBase;
        else if (!membership.SaleCommissionEmployeeId.HasValue)
            membership.FirstSaleCommissionOutcome = MembershipFirstSaleCommissionOutcome.NoRecipient;
        else
        {
            DateOnly saleDate = (await _calendars.GetCalendar(organizationId)).LocalDate(now);
            CommissionEntry created = await TryEarnSale(uow, organizationId, membership.SaleCommissionEmployeeId.Value,
                CommissionSourceType.MembershipSale, CommissionSubjectType.MembershipPlan, membership.MembershipPlanId, null, membershipId,
                membership.SoldCompanyId, baseAmount, saleDate,
                await NextSourceVersion(uow, organizationId, CommissionSourceType.MembershipSale, membershipId), null);
            membership.FirstSaleCommissionOutcome = created != null ? MembershipFirstSaleCommissionOutcome.Earned : MembershipFirstSaleCommissionOutcome.NoRule;
        }
        await uow.Context.SaveChangesAsync();
    }

    public async Task SyncPolicyFeeCommission(IUnitOfWork uow, Guid organizationId, Guid userId, Guid participationId)
    {
        BookingSegmentParticipation participation = await uow.Context.BookingSegmentParticipations
            .Include(p => p.PolicyConsequences)
            .Include(p => p.PackageConsumptions)
            .Include(p => p.MembershipCoverage)
            .Include(p => p.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == participationId);
        if (participation == null)
            return;

        List<CommissionEntry> entries = await _entryHandler.GetForSource(uow, organizationId, CommissionSourceType.PolicyFee, participationId);
        ParticipationPolicyConsequence active = ParticipationOccupancy.Occupies(participation.Status)
            ? null
            : PolicyConsequences.ActiveOf(participation);
        ParticipationSettlement settlement = ParticipationSettlement.Of(participation);
        // Q38: naknada (ne kredit ni jedinica paketa) plaćena U CIJELOSTI.
        bool feePaid = active != null
                       && active.CalculatedFeeAmount > 0m
                       && settlement.MonetaryDue > 0m
                       && settlement.SettledAmount >= settlement.MonetaryDue;

        foreach (CommissionEntry entry in entries.Where(e => e.Status == CommissionEntryStatus.Earned))
        {
            if (feePaid && entry.ParticipationPolicyConsequenceId == active.Id)
                continue;
            string reason = active == null || entry.ParticipationPolicyConsequenceId != active.Id
                ? "Naknada je oproštena ili poništena"
                : "Naknada više nije plaćena u cijelosti";
            await Reverse(uow, userId, entry, reason);
        }

        if (!feePaid || entries.Any(e => e.Status == CommissionEntryStatus.Earned && e.ParticipationPolicyConsequenceId == active.Id))
            return;

        CommissionBaseSettings settings = await LoadSettings(organizationId);
        if (settings.LateCancellation != CommissionLateCancellationMode.WhenFeePaid)
            return;

        Booking booking = await _appointmentHandler.GetBookingById(uow, organizationId, participation.BookingId);
        if (booking?.Appointment == null || booking.Appointment.Form == AppointmentForm.Group)
            return;

        ParticipationExecutionContext execution = ExecutionContextResolver.ForParticipation(booking.Appointment, booking, participation);
        DateOnly sessionDate = await SessionDate(organizationId, execution);
        decimal fee = settlement.MonetaryDue;
        int sourceVersion = entries.Count == 0 ? 0 : entries.Max(e => e.SourceVersion) + 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (Guid employeeId in execution.EmployeeIds.Distinct().OrderBy(id => id))
        {
            // Isto pravilo kao za odrađenu sesiju (usluga ima prednost pred općim); postotak od naknade, Fixed najviše naknada.
            RuleChoice choice = await ChooseServiceRule(organizationId, employeeId, execution.ServiceId, sessionDate, includeGeneral: true);
            if (choice == null)
                continue;

            decimal amount = Calculate(choice.CalculationType, choice.Value, fee);
            bool capped = choice.CalculationType == CommissionCalculationType.Fixed && amount > fee;
            await TryAdd(uow, new CommissionEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                CompanyId = execution.CompanyId,
                CommissionRuleId = choice.RuleId,
                SourceType = CommissionSourceType.PolicyFee,
                AppointmentId = execution.AppointmentId,
                BookingId = participation.BookingId,
                BookingSegmentParticipationId = participation.Id,
                ParticipationPolicyConsequenceId = active.Id,
                BaseAmount = fee,
                CalculationType = choice.CalculationType,
                RuleValue = choice.Value,
                CommissionAmount = capped ? fee : amount,
                PaymentSource = CommissionPaymentSource.Direct,
                AppliedRuleScope = choice.Scope,
                RuleEvaluation = choice.EvaluationJson,
                WasCapped = capped,
                Status = CommissionEntryStatus.Earned,
                SourceVersion = sourceVersion,
                EarnedAt = now,
                CreatedAt = now
            });
        }
    }

    public async Task<bool> SetMembershipSaleCommissionEmployee(
        IUnitOfWork uow, Guid organizationId, Guid userId, Guid membershipId, Guid? employeeId, string via)
    {
        ClientMembership membership = await _membershipHandler.GetForUpdate(uow, organizationId, membershipId)
            ?? throw new NotFoundAppException("ClientMembership", membershipId);
        if (membership.FirstSaleSettledAt != null)
        {
            List<CommissionEntry> entries = await _entryHandler.GetForSource(uow, organizationId, CommissionSourceType.MembershipSale, membershipId);
            if (entries.Any(e => e.Status == CommissionEntryStatus.Earned))
                throw new BusinessRuleException(ErrorCodes.CommissionSaleAlreadyEarned,
                    "Provizija na prodaju članarine je već nastala — korisnik se mijenja samo korekcijom provizije (uz razlog).");
            throw new BusinessRuleException(ErrorCodes.CommissionSaleAlreadyEvaluated,
                "Provizija na prvu prodaju je već evaluirana — ako nije nastala jer korisnika nije bilo, korisnik se dodjeljuje naknadno (commissions.manage, razlog).");
        }
        if (employeeId.HasValue)
            await EnsureSelectableEmployee(organizationId, employeeId.Value);
        if (membership.SaleCommissionEmployeeId == employeeId)
            return false;

        Guid? old = membership.SaleCommissionEmployeeId;
        membership.SaleCommissionEmployeeId = employeeId;
        membership.UpdatedAt = DateTimeOffset.UtcNow;
        membership.UpdatedBy = userId;
        _membershipHandler.AddAudit(uow, MembershipTimelines.Audit(membership, userId, "SaleCommissionEmployeeChanged",
            old?.ToString(), employeeId?.ToString(), via));
        await uow.Context.SaveChangesAsync();
        return true;
    }

    /// <summary>Vidi ICommissionLedgerService za puni ugovor. Namjerno BEZ catch/throw na "nema što reverzirati" —
    /// no-op je ispravan odgovor i za "nikad nije bilo primjenjivog pravila" i za "već reverzirano" (idempotentan retry).</summary>
    public async Task ReverseForIndividualServiceCorrection(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation)
    {
        // Phase M1G: reverziraju se SVI aktivni zapisi sudjelovanja (svaki zaposlenik segmenta), ne samo jedan.
        List<CommissionEntry> entries = await _entryHandler.GetActiveForParticipation(uow, organizationId, participation.Id.GetValueOrDefault());
        foreach (CommissionEntry entry in entries)
            await Reverse(uow, userId, entry, null);
    }

    /// <summary>P2 (2F) — JEDINI put storna provizije (Earned → Reversed na istom retku, snapshot netaknut); izvještaji ga broje
    /// kao negativan iznos u razdoblju ReversedAt. Koriste ga korekcija completiona, Q38 i korekcija korisnika (Q50); budući
    /// povrat novca (P3) i poništavanje prodaje (Q51a) zovu isti put.</summary>
    private async Task Reverse(IUnitOfWork uow, Guid userId, CommissionEntry entry, string reason)
    {
        entry.Status = CommissionEntryStatus.Reversed;
        entry.ReversedAt = DateTimeOffset.UtcNow;
        entry.ReversedBy = userId;
        entry.ReversalReason = reason;
        await _entryHandler.Update(uow, entry);
    }

    /// <summary>Namjerno BEZ catch(DbUpdateException) ovdje — Postgres transakcija se prekida (aborted) nakon
    /// prve povrede constrainta, pa bi gutanje iznimke usred iste transakcije ostavilo naredni CommitAsync
    /// pozivatelja da propadne na "current transaction is aborted" umjesto na stvaran uzrok. Commission zapis mora biti
    /// atomaran s prijelazom koji ga zarađuje. Unique indeksi na CommissionEntry su zadnja linija obrane za stvarnu konkurenciju.</summary>
    private async Task TryAdd(IUnitOfWork uow, CommissionEntry entry)
    {
        await _entryHandler.Add(uow, entry);
    }

    private static decimal Calculate(CommissionCalculationType calculationType, decimal value, decimal baseAmount)
    {
        return calculationType == CommissionCalculationType.Percentage ? baseAmount * value / 100m : value;
    }

    #endregion

    #region Izbor pravila (Vagaro: pravilo za uslugu ima prednost pred općim)

    /// <summary>Primijenjeno pravilo (izračun, vrijednost, razina) i objašnjenje izbora za zapis provizije.</summary>
    private sealed record RuleChoice(Guid RuleId, CommissionRuleScope Scope, CommissionCalculationType CalculationType, decimal Value, string EvaluationJson)
    {
        public static RuleChoice ForSubject(CommissionRule rule, List<CommissionRuleEvaluationItemDto> notApplied, string reason) =>
            Create(rule, CommissionRuleScope.Subject, rule.CalculationType.GetValueOrDefault(), rule.Value.GetValueOrDefault(), reason, notApplied);

        public static RuleChoice Create(CommissionRule rule, CommissionRuleScope scope, CommissionCalculationType type, decimal value,
            string reason, List<CommissionRuleEvaluationItemDto> notApplied)
        {
            CommissionRuleEvaluationDto evaluation = new()
            {
                Applied = Item(rule, scope, type, value, reason),
                NotApplied = notApplied ?? new List<CommissionRuleEvaluationItemDto>()
            };
            return new RuleChoice(rule.Id.GetValueOrDefault(), scope, type, value, JsonSerializer.Serialize(evaluation));
        }
    }

    private static CommissionRuleEvaluationItemDto Item(CommissionRule rule, CommissionRuleScope scope, CommissionCalculationType type, decimal value, string reason) => new()
    {
        RuleId = rule.Id.GetValueOrDefault(),
        Scope = scope,
        EffectiveFrom = EffectiveFromOf(rule),
        CalculationType = type,
        Value = value,
        Reason = reason
    };

    /// <summary>Jednoznačna prednost: (1) pravilo za uslugu važeće na datum — "Bez provizije" znači da provizije nema i opće pravilo
    /// se NE primjenjuje; (2) inače opće pravilo zaposlenika (samo individualne usluge), razina bez praga. Deaktivirana verzija
    /// pravila za uslugu znači da od datuma deaktivacije pravila za uslugu nema (vrijedi opće pravilo, ako postoji). Null = nema
    /// provizije.</summary>
    private async Task<RuleChoice> ChooseServiceRule(Guid organizationId, Guid employeeId, Guid serviceId, DateOnly date, bool includeGeneral)
    {
        CommissionRule service = await _ruleHandler.GetVersionOn(
            organizationId, employeeId, CommissionRuleKind.Performance, CommissionSubjectType.Service, serviceId, date);
        CommissionRule general = includeGeneral
            ? await _ruleHandler.GetVersionOn(organizationId, employeeId, CommissionRuleKind.Performance, CommissionSubjectType.AllServices, null, date)
            : null;
        bool serviceApplies = service != null && service.AppliesOn(date);
        CommissionRuleTier generalTier = general != null && general.AppliesOn(date)
            ? general.Tiers.Where(t => t.FromRevenue == 0m).SingleOrDefault()
            : null;

        if (serviceApplies)
        {
            if (service.CalculationType == CommissionCalculationType.None)
                return null; // izričito "Bez provizije": ne nastaje ništa, opće pravilo se ne primjenjuje
            List<CommissionRuleEvaluationItemDto> skipped = generalTier == null
                ? new List<CommissionRuleEvaluationItemDto>()
                : new List<CommissionRuleEvaluationItemDto> { Item(general, CommissionRuleScope.AllServices, generalTier.CalculationType, generalTier.Value, SubjectWinsReason) };
            return RuleChoice.ForSubject(service, skipped, "Pravilo zaposlenika za ovu uslugu.");
        }

        if (generalTier == null)
            return null;

        List<CommissionRuleEvaluationItemDto> notApplied = new();
        if (service != null)
            notApplied.Add(Item(service, CommissionRuleScope.Subject, service.CalculationType.GetValueOrDefault(), service.Value.GetValueOrDefault(),
                $"Pravilo za uslugu je deaktivirano od {service.DeactivatedFrom:yyyy-MM-dd}."));
        return RuleChoice.Create(general, CommissionRuleScope.AllServices, generalTier.CalculationType, generalTier.Value,
            "Nema pravila za ovu uslugu — primijenjeno opće pravilo zaposlenika.", notApplied);
    }

    #endregion

    #region Postavke i izvor pokrića

    private sealed record CommissionBaseSettings(bool DeductDiscounts, bool DeductMembershipDiscounts, CommissionLateCancellationMode LateCancellation);

    private async Task<CommissionBaseSettings> LoadSettings(Guid organizationId)
    {
        OrganizationSettings settings = await _settingsHandler.GetByOrganizationId(organizationId);
        return new CommissionBaseSettings(
            settings?.CommissionDeductDiscounts ?? false,
            settings?.CommissionDeductMembershipDiscounts ?? false,
            settings?.CommissionLateCancellation ?? CommissionLateCancellationMode.Never);
    }

    /// <summary>Lokalni datum sesije u kalendaru poslovnice termina (isti kalendar kao valjanost paketa i prozori limita).</summary>
    private async Task<DateOnly> SessionDate(Guid organizationId, SegmentExecutionContext execution) =>
        (await _calendars.GetCompanyCalendar(organizationId, execution.CompanyId)).LocalDate(execution.StartsAt);

    /// <summary>Način plaćanja iz ledgera pokrića u trenutku nastanka (snapshot, informativno): aktivan claim članarine →
    /// Membership; aktivna potrošnja paketa za izvršenje usluge → Package; inače Direct.</summary>
    private static async Task<(CommissionPaymentSource, Guid?)> ResolvePaymentSource(IUnitOfWork uow, Guid organizationId, BookingSegmentParticipation participation)
    {
        Guid participationId = participation.Id.GetValueOrDefault();
        Guid? membershipId = await uow.Context.MembershipUsages
            .Where(u => u.OrganizationId == organizationId && u.ParticipationId == participationId &&
                        u.EntryType == MembershipUsageEntryType.Claim && u.IsActive)
            .Select(u => (Guid?)u.ClientMembershipId)
            .FirstOrDefaultAsync();
        if (membershipId.HasValue)
            return (CommissionPaymentSource.Membership, membershipId);

        Guid? clientPackageId = await uow.Context.PackageConsumptions
            .Where(c => c.OrganizationId == organizationId && c.BookingSegmentParticipationId == participationId &&
                        c.Status == PackageConsumptionStatus.Consumed && c.Trigger == PackageConsumptionTrigger.ServiceCompletion)
            .Select(c => (Guid?)c.ClientPackageId)
            .FirstOrDefaultAsync();
        return clientPackageId.HasValue
            ? (CommissionPaymentSource.Package, clientPackageId)
            : (CommissionPaymentSource.Direct, null);
    }

    #endregion
}
