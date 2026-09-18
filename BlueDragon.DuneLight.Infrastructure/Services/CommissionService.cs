using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Commissions;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces.Commissions;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Employees;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using ProductEntity = BlueDragon.DuneLight.Infrastructure.Domain.Models.Products.Product;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Vidi ICommissionRuleService/ICommissionService/ICommissionLedgerService za domenske napomene. Jedna klasa
/// implementira sva tri sučelja — isti obrazac kao PaymentService (IPaymentService + IPaymentLedgerService).
///
/// GENERACIJSKA ODLUKA (Group Service): commission izvor je CIJELI odrađeni grupni termin
/// (Appointment.Status=Completed, vidi AppointmentService.CompleteGroupAppointment), NE pojedina odrađena
/// Booking prisutnost (Booking.Status=Completed po sudioniku, vidi BookingService.SetStatus). Razlog: trener
/// grupne nastave je tipično plaćen za ODRŽAVANJE termina, ne po broju sudionika — množenje Fixed iznosa brojem
/// prisutnih bi zahtijevalo eksplicitnu poslovnu potvrdu koju ovaj kodbaza ne izražava nigdje (Group nema
/// per-occurrence revenue/attendance-fee koncept, vidi Group.cs/Appointment.cs), a Percentage bi zahtijevao
/// nedvosmislenu osnovicu koja ovdje ne postoji (Group Appointment nema svoju cijenu, samo Booking.Amount po
/// klijentu preko istog IPricingService poziva kao Individual). Ova odluka ujedno znači da grupna provizija
/// NEMA reverzijsku putanju: BookingService.ApplyGroupTransition (Completed/NoShow -> Confirmed poništenje
/// check-ina PO SUDIONIKU) ne dira Appointment.Status i stoga ne dira komisijski izvor.
///
/// ISPRAVLJENO (bilo "poznata postojeća praznina"): AppointmentService.Cancel/MarkNoShow (ChangeToTerminalStatus)
/// sada odbija Completed -> Cancelled/NoShow (ErrorCodes.AlreadyCompleted) umjesto da tiho ostavi Earned
/// CommissionEntry uz Appointment koji više ne izgleda odrađen — vidi ChangeToTerminalStatus. Grupna provizija i
/// dalje NEMA reverzijsku putanju jer Appointment-razina completion nema legitiman "undo" u trenutnom lifecycleu
/// — fix zatvara nevaljan prijelaz umjesto da izmišlja reverziju za njega.
///
/// INDIVIDUAL BOOKING REVERZIJA (ReverseForIndividualServiceCorrection, dodano uz P1 korekcijski tok): za razliku
/// od Group, individualni Booking-completion IMA legitiman "undo" — BookingService.ApplyIndividualCompletionCorrection
/// (Individual Booking Completed -> Confirmed, administrativna korekcija pogrešnog check-ina). Reverzija identificira
/// izvor deterministički preko BookingId (ICommissionEntryHandler.GetActiveForBooking, Status=Earned), NE po
/// iznosu/datumu/zaposleniku. CommissionEntry.SourceVersion (Booking.StatusVersion u trenutku zarade, vidi
/// CommissionEntry.cs) daje svakoj completion-pojavi zasebni identitet, tako da ponovni completion istog Bookinga
/// nakon korekcije zaradi NOVI Earned zapis bez sudara s (sad Reversed) starim — vidi Migration_2026_09_25.
/// </summary>
public class CommissionService : ICommissionRuleService, ICommissionService, ICommissionLedgerService
{
    private readonly ICommissionRuleHandler _ruleHandler;
    private readonly ICommissionEntryHandler _entryHandler;
    private readonly IEmployeeHandler _employeeHandler;
    private readonly IServiceHandler _serviceHandler;
    private readonly IProductHandler _productHandler;
    private readonly IPackageHandler _packageHandler;

    public CommissionService(
        ICommissionRuleHandler ruleHandler,
        ICommissionEntryHandler entryHandler,
        IEmployeeHandler employeeHandler,
        IServiceHandler serviceHandler,
        IProductHandler productHandler,
        IPackageHandler packageHandler)
    {
        _ruleHandler = ruleHandler;
        _entryHandler = entryHandler;
        _employeeHandler = employeeHandler;
        _serviceHandler = serviceHandler;
        _productHandler = productHandler;
        _packageHandler = packageHandler;
    }

    #region Rule CRUD

    public async Task<List<CommissionRuleDto>> GetList(Guid organizationId, CommissionRuleQuery query)
    {
        List<CommissionRule> rules = await _ruleHandler.GetList(organizationId, query.EmployeeId, query.SubjectType, query.IsActive);
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

        await ValidateSubjectAndPercentageRule(organizationId, request.SubjectType, request.ServiceId, request.ProductId, request.PackageId, request.CalculationType);
        ValidateValue(request.CalculationType, request.Value);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CommissionRule rule = new CommissionRule
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = request.EmployeeId,
            SubjectType = request.SubjectType,
            ServiceId = request.ServiceId,
            ProductId = request.ProductId,
            PackageId = request.PackageId,
            CalculationType = request.CalculationType,
            Value = request.Value,
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
                "Zaposlenik već ima aktivno pravilo provizije za ovaj predmet.");
        }

        return await GetById(organizationId, rule.Id.GetValueOrDefault());
    }

    public async Task<CommissionRuleDto> Update(Guid organizationId, Guid userId, Guid id, CommissionRuleUpdateRequest request)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        await ValidateSubjectAndPercentageRule(organizationId, rule.SubjectType, rule.ServiceId, rule.ProductId, rule.PackageId, request.CalculationType);
        ValidateValue(request.CalculationType, request.Value);

        rule.CalculationType = request.CalculationType;
        rule.Value = request.Value;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = userId;

        await _ruleHandler.Update(rule);

        return await GetById(organizationId, id);
    }

    public async Task<CommissionRuleDto> SetActive(Guid organizationId, Guid userId, Guid id, bool isActive)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        // Reaktivacija mora proći ISTU provjeru kao Create/Update — Service.ExecutionMode se mogao promijeniti
        // Individual->Group dok je pravilo bilo neaktivno, i bez ove provjere bi se Percentage pravilo moglo
        // ponovno aktivirati za grupnu uslugu (koju Group commission generacija tiho preskače jer podržava samo
        // Fixed, vidi CommissionService domensku napomenu). Deaktivacija (isActive=false) ne treba ovu provjeru.
        if (isActive)
            await ValidateSubjectAndPercentageRule(organizationId, rule.SubjectType, rule.ServiceId, rule.ProductId, rule.PackageId, rule.CalculationType);

        rule.IsActive = isActive;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        rule.UpdatedBy = userId;

        try
        {
            await _ruleHandler.Update(rule);
        }
        catch (DbUpdateException)
        {
            throw new BusinessRuleException(
                ErrorCodes.CommissionRuleAlreadyExists,
                "Zaposlenik već ima drugo aktivno pravilo provizije za ovaj predmet.");
        }

        return await GetById(organizationId, id);
    }

    public async Task Delete(Guid organizationId, Guid id)
    {
        CommissionRule rule = await _ruleHandler.GetById(organizationId, id);
        if (rule == null)
            throw new NotFoundAppException("CommissionRule", id);

        bool isReferenced = await _ruleHandler.IsReferenced(organizationId, id);
        if (isReferenced)
            throw new BusinessRuleException(
                ErrorCodes.ReferencedCannotDelete,
                "Pravilo provizije ima povijest zarada i ne može se trajno obrisati — deaktivirajte ga umjesto toga.");

        await _ruleHandler.Delete(rule);
    }

    /// <summary>Provjerava da je točno jedan predmet popunjen prema SubjectType, da pripada ovoj organizaciji, i
    /// da Percentage nije zatražen za Service.ExecutionMode=Group (vidi spec section 16/54 — nema nedvosmislene
    /// per-occurrence osnovice, samo Fixed je podržan za grupne usluge).</summary>
    private async Task ValidateSubjectAndPercentageRule(
        Guid organizationId, CommissionSubjectType subjectType, Guid? serviceId, Guid? productId, Guid? packageId,
        CommissionCalculationType calculationType)
    {
        switch (subjectType)
        {
            case CommissionSubjectType.Service:
                if (!serviceId.HasValue || productId.HasValue || packageId.HasValue)
                    throw new ValidationAppException("Za SubjectType=Service mora biti popunjen isključivo ServiceId.");

                Service service = await _serviceHandler.GetById(organizationId, serviceId.Value);
                if (service == null)
                    throw new NotFoundAppException("Service", serviceId.Value);

                if (calculationType == CommissionCalculationType.Percentage && service.ExecutionMode == ServiceExecutionMode.Group)
                    throw new BusinessRuleException(
                        ErrorCodes.CommissionGroupPercentageNotSupported,
                        "Postotna provizija nije podržana za grupne usluge (nema nedvosmislene osnovice po terminu) — koristite Fixed.");
                break;

            case CommissionSubjectType.Product:
                if (!productId.HasValue || serviceId.HasValue || packageId.HasValue)
                    throw new ValidationAppException("Za SubjectType=Product mora biti popunjen isključivo ProductId.");

                ProductEntity product = await _productHandler.GetById(organizationId, productId.Value);
                if (product == null)
                    throw new NotFoundAppException("Product", productId.Value);
                break;

            case CommissionSubjectType.Package:
                if (!packageId.HasValue || serviceId.HasValue || productId.HasValue)
                    throw new ValidationAppException("Za SubjectType=Package mora biti popunjen isključivo PackageId.");

                Package package = await _packageHandler.GetById(organizationId, packageId.Value);
                if (package == null)
                    throw new NotFoundAppException("Package", packageId.Value);
                break;

            default:
                throw new ValidationAppException("Nepoznat SubjectType.");
        }
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
            SubjectType = rule.SubjectType,
            ServiceId = rule.ServiceId,
            ServiceName = rule.Service?.Name,
            ProductId = rule.ProductId,
            ProductName = rule.Product?.Name,
            PackageId = rule.PackageId,
            PackageName = rule.Package?.Name,
            CalculationType = rule.CalculationType,
            Value = rule.Value,
            IsActive = rule.IsActive,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt
        };
    }

    #endregion

    #region Queries

    public async Task<PagedResult<CommissionEntryDto>> GetEntries(Guid organizationId, CommissionEntryQuery query)
    {
        if (query.To < query.From)
            throw new ValidationAppException("'To' ne smije biti prije 'From'.");

        (List<CommissionEntry> items, int totalCount) = await _entryHandler.GetPaged(organizationId, query);

        List<CommissionEntryDto> dtos = items.Select(ToDto).ToList();
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

    private static CommissionEntryDto ToDto(CommissionEntry entry)
    {
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
            CheckoutItemId = entry.CheckoutItemId,
            BaseAmount = entry.BaseAmount,
            CalculationType = entry.CalculationType,
            RuleValue = entry.RuleValue,
            CommissionAmount = entry.CommissionAmount,
            Status = entry.Status,
            EarnedAt = entry.EarnedAt
        };
    }

    #endregion

    #region Ledger generation (called from AppointmentService/CheckoutService within their own transaction)

    public async Task GenerateForIndividualServiceCompletion(IUnitOfWork uow, Guid organizationId, Appointment appointment, Booking booking)
    {
        if (!appointment.EmployeeId.HasValue)
            return;

        CommissionRule rule = await _ruleHandler.GetActiveForSubject(
            organizationId, appointment.EmployeeId.Value, CommissionSubjectType.Service, appointment.ServiceId, null, null);
        if (rule == null)
            return;

        decimal commissionAmount = Calculate(rule.CalculationType, rule.Value, booking.Amount);

        await TryAdd(uow, new CommissionEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = appointment.EmployeeId.Value,
            CompanyId = appointment.CompanyId,
            CommissionRuleId = rule.Id.GetValueOrDefault(),
            SourceType = CommissionSourceType.IndividualService,
            AppointmentId = appointment.Id,
            BookingId = booking.Id,
            BaseAmount = booking.Amount,
            CalculationType = rule.CalculationType,
            RuleValue = rule.Value,
            CommissionAmount = commissionAmount,
            Status = CommissionEntryStatus.Earned,
            // Booking.StatusVersion NAKON prijelaza u Completed (pozivatelj TrySetStatus prije ovog poziva, vidi
            // FK zahtjev u domenskoj napomeni) — daje ovoj completion-pojavi zaseban identitet naspram eventualnog
            // narednog completiona nakon korekcije (vidi CommissionEntry.cs SourceVersion napomenu).
            SourceVersion = booking.StatusVersion,
            EarnedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task GenerateForGroupServiceCompletion(IUnitOfWork uow, Guid organizationId, Appointment appointment)
    {
        if (!appointment.EmployeeId.HasValue)
            return;

        CommissionRule rule = await _ruleHandler.GetActiveForSubject(
            organizationId, appointment.EmployeeId.Value, CommissionSubjectType.Service, appointment.ServiceId, null, null);

        // Defensive — CommissionRuleService već odbija Percentage za Group-mode Service kod kreiranja/izmjene
        // pravila, ovo je samo backstop ako se Service.ExecutionMode promijeni nakon što je pravilo stvoreno.
        if (rule == null || rule.CalculationType != CommissionCalculationType.Fixed)
            return;

        await TryAdd(uow, new CommissionEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = appointment.EmployeeId.Value,
            CompanyId = appointment.CompanyId,
            CommissionRuleId = rule.Id.GetValueOrDefault(),
            SourceType = CommissionSourceType.GroupService,
            AppointmentId = appointment.Id,
            BookingId = null,
            BaseAmount = 0m,
            CalculationType = rule.CalculationType,
            RuleValue = rule.Value,
            CommissionAmount = rule.Value,
            Status = CommissionEntryStatus.Earned,
            EarnedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task GenerateForCheckoutCompletion(IUnitOfWork uow, Guid organizationId, Guid completedByUserId, Checkout checkout)
    {
        Employee employee = await _employeeHandler.GetByUserId(organizationId, completedByUserId);
        if (employee == null || !employee.IsActive)
            return;

        bool hasSalesItems = checkout.Items.Any(i => i.Type == CheckoutItemType.Product || i.Type == CheckoutItemType.Package);
        if (!hasSalesItems)
            return;

        // Jedan upit za SVA aktivna pravila ovog Employeea umjesto jednog upita po CheckoutItem stavci (vidi spec
        // section 15/61 N+1 upozorenje) — DB partial unique indeks jamči najviše jedno aktivno pravilo po
        // (SubjectType, Product/PackageId), pa je mapiranje po ključu bez sudara. Upit je već filtriran na
        // Organization+Employee, pa mapiranje u memoriji ne može slučajno primijeniti tuđe/pogrešno pravilo
        // (vidi spec section 16).
        List<CommissionRule> activeRules = await _ruleHandler.GetAllActiveForEmployee(organizationId, employee.Id.GetValueOrDefault());
        Dictionary<(CommissionSubjectType SubjectType, Guid SubjectId), CommissionRule> rulesBySubject = activeRules
            .Where(r => r.SubjectType == CommissionSubjectType.Product || r.SubjectType == CommissionSubjectType.Package)
            .ToDictionary(r => (
                r.SubjectType,
                r.SubjectType == CommissionSubjectType.Product ? r.ProductId.GetValueOrDefault() : r.PackageId.GetValueOrDefault()));

        foreach (CheckoutItem item in checkout.Items)
        {
            CommissionSubjectType subjectType;
            CommissionSourceType sourceType;
            Guid subjectId;
            switch (item.Type)
            {
                case CheckoutItemType.Product:
                    subjectType = CommissionSubjectType.Product;
                    sourceType = CommissionSourceType.ProductSale;
                    subjectId = item.ProductId.GetValueOrDefault();
                    break;
                case CheckoutItemType.Package:
                    subjectType = CommissionSubjectType.Package;
                    sourceType = CommissionSourceType.PackageSale;
                    subjectId = item.PackageId.GetValueOrDefault();
                    break;
                default:
                    continue;
            }

            if (!rulesBySubject.TryGetValue((subjectType, subjectId), out CommissionRule rule))
                continue;

            decimal commissionAmount = Calculate(rule.CalculationType, rule.Value, item.Amount);

            await TryAdd(uow, new CommissionEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employee.Id.GetValueOrDefault(),
                CompanyId = checkout.CompanyId,
                CommissionRuleId = rule.Id.GetValueOrDefault(),
                SourceType = sourceType,
                CheckoutItemId = item.Id,
                BaseAmount = item.Amount,
                CalculationType = rule.CalculationType,
                RuleValue = rule.Value,
                CommissionAmount = commissionAmount,
                Status = CommissionEntryStatus.Earned,
                EarnedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    /// <summary>Vidi ICommissionLedgerService za puni ugovor. Namjerno BEZ catch/throw na "nema što reverzirati" —
    /// no-op je ispravan odgovor i za "nikad nije bilo primjenjivog pravila" i za "već reverzirano" (idempotentan
    /// retry), pozivatelj (BookingService) ne treba razlikovati ta dva slučaja.</summary>
    public async Task ReverseForIndividualServiceCorrection(IUnitOfWork uow, Guid organizationId, Guid userId, Booking booking)
    {
        CommissionEntry entry = await _entryHandler.GetActiveForBooking(uow, organizationId, booking.Id.GetValueOrDefault());
        if (entry == null)
            return;

        entry.Status = CommissionEntryStatus.Reversed;
        entry.ReversedAt = DateTimeOffset.UtcNow;
        entry.ReversedBy = userId;

        await _entryHandler.Update(uow, entry);
    }

    /// <summary>Namjerno BEZ catch(DbUpdateException) ovdje — Postgres transakcija se prekida (aborted) nakon
    /// prve povrede constrainta, pa bi gutanje iznimke usred iste transakcije ostavilo naredni CommitAsync
    /// pozivatelja da propadne na "current transaction is aborted" umjesto na stvaran uzrok, i riskiralo
    /// nedosljedno stanje suprotno spec section 28 (commission zapis mora biti atomaran s prijelazom koji ga
    /// zarađuje — ako zapis ne uspije, ni prijelaz se ne smije committati). Unique indeksi na CommissionEntry
    /// (vidi migraciju) su zadnja linija obrane za stvarnu konkurenciju — u praksi je nedostižna jer pozivatelji
    /// (AppointmentService.CompleteExisting/CompleteGroupAppointment, CheckoutService.Complete) već zaključavaju
    /// izvorni redak (FOR UPDATE) i provjeravaju status PRIJE nego što dođu do ove metode, pa dva konkurentna
    /// zahtjeva nad ISTIM izvorom nikad ne trče ovaj kod istovremeno (drugi vidi već-Completed/Not-Open i baca
    /// prije ikakvog commission poziva). Ako indeks ipak nekad opali, cijela transakcija (uklj. izvorni
    /// completion) se ispravno vraća natrag — sigurnije od tihog "pola uspjelo".</summary>
    private async Task TryAdd(IUnitOfWork uow, CommissionEntry entry)
    {
        await _entryHandler.Add(uow, entry);
    }

    private static decimal Calculate(CommissionCalculationType calculationType, decimal value, decimal baseAmount)
    {
        return calculationType == CommissionCalculationType.Percentage ? baseAmount * value / 100m : value;
    }

    #endregion
}
