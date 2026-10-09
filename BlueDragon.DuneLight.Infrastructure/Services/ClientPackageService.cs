using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Interfaces.Catalog;
using BlueDragon.DuneLight.Core.Interfaces.Clients;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Prodaja i pregled paketa klijenta te (Phase D3B3A) JEDINI ledger potrošnje paketa — vidi
/// IPackageConsumptionLedgerService (isti obrazac kao CommissionService : ICommissionLedgerService).
/// </summary>
public class ClientPackageService : IClientPackageService, IPackageConsumptionLedgerService
{
    private const int MaxConcurrencyRetries = 3;

    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly ICompanyHandler _companyHandler;
    private readonly IPricingService _pricingService;
    private readonly IOrganizationCalendarService _organizationCalendarService;
    private readonly IOrganizationSettingsService _organizationSettingsService;
    private readonly ICheckoutHandler _checkoutHandler;
    private readonly IBookingSegmentParticipationHandler _participationHandler;
    private readonly IGrantResolver _grantResolver;
    private readonly TimeProvider _timeProvider;

    public ClientPackageService(
        IClientPackageHandler clientPackageHandler,
        IClientHandler clientHandler,
        IPackageHandler packageHandler,
        ICompanyHandler companyHandler,
        IPricingService pricingService,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService,
        ICheckoutHandler checkoutHandler,
        IBookingSegmentParticipationHandler participationHandler,
        IGrantResolver grantResolver,
        TimeProvider timeProvider)
    {
        _grantResolver = grantResolver;
        _checkoutHandler = checkoutHandler;
        _participationHandler = participationHandler;
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _clientPackageHandler = clientPackageHandler;
        _clientHandler = clientHandler;
        _packageHandler = packageHandler;
        _companyHandler = companyHandler;
        _pricingService = pricingService;
        _timeProvider = timeProvider;
    }

    public async Task<ClientPackageDto> Create(Guid organizationId, Guid userId, Guid clientId, ClientPackageCreateRequest request)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, clientId);
        if (client == null)
            throw new NotFoundAppException("Client", clientId);
        if (client.IsAnonymized)
            throw new BusinessRuleException(ErrorCodes.ClientAnonymized, "Klijent je anonimiziran i ne može mu se izdati novi paket.");
        if (!client.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactiveClient, "Klijent nije aktivan i ne može mu se izdati novi paket.");

        Package package = await _packageHandler.GetById(organizationId, request.PackageId);
        if (package == null)
            throw new NotFoundAppException("Package", request.PackageId);
        if (!package.IsActive)
            throw new BusinessRuleException(ErrorCodes.InactivePackage, $"Paket '{package.Name}' nije aktivan i ne može se prodati.");

        if (request.CompanyId.HasValue)
        {
            Company company = await _companyHandler.GetById(organizationId, request.CompanyId.Value);
            if (company == null)
                throw new NotFoundAppException("Company", request.CompanyId.Value);
            if (!company.IsActive)
                throw new BusinessRuleException(ErrorCodes.InactiveCompany, $"Tvrtka '{company.Name}' nije aktivna.");
        }

        // T1-7: PurchaseDate je POSLOVNI dan kupnje (DateOnly) — zadana vrijednost je današnji dan po poslovnom satu u zoni
        // poslovnice prodaje (organizacije kad prodaja stvarno nema poslovnicu); trenutak prodaje ostaje CreatedAt.
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = (await _organizationCalendarService.GetCompanyOrOrganizationCalendar(organizationId, request.CompanyId)).LocalDate(now);
        DateOnly purchaseDate = request.PurchaseDate ?? today;
        bool backdated = await EnsurePurchaseDateAllowed(organizationId, userId, purchaseDate, today);

        decimal paidPrice = request.PaidPrice ?? await ResolveSuggestedPrice(organizationId, request.PackageId, request.CompanyId, purchaseDate);

        DateOnly validUntilDate = PackageExpiryCalculator.ForSale(package, purchaseDate);
        // T1-9: paket koji bi pri upisu već bio istekao se ne upisuje (uvoz povijesti samo uz posebnu odluku). Vrijedi i za
        // današnju kupnju paketa s fiksnim datumom isteka koji je prošao.
        if (validUntilDate < today)
            throw new BusinessRuleException(ErrorCodes.PackageExpiredAtIssue,
                $"Paket kupljen {purchaseDate:dd.MM.yyyy.} vrijedi do {validUntilDate:dd.MM.yyyy.}, što je prije današnjeg dana ({today:dd.MM.yyyy.}) — istekao paket se ne može upisati.",
                new { purchaseDate, validUntilDate, today });

        Guid clientPackageId = Guid.NewGuid();
        ClientPackage clientPackage = new ClientPackage
        {
            Id = clientPackageId,
            OrganizationId = organizationId,
            ClientId = clientId,
            PackageId = request.PackageId,
            PurchaseDate = purchaseDate,
            PaidPrice = paidPrice,
            EntryMode = package.EntryMode,
            TotalEntryCount = package.TotalEntryCount,
            RemainingSharedEntries = package.EntryMode == PackageEntryMode.SharedPool ? package.TotalEntryCount : null,
            ValidityType = package.ValidityType,
            ValidUntilDate = validUntilDate,
            Status = ClientPackageStatus.Active,
            CreatedAt = now,
            CreatedBy = userId
        };

        foreach (PackageServiceItem item in package.Services)
        {
            clientPackage.ServiceEntries.Add(new ClientPackageServiceEntry
            {
                Id = Guid.NewGuid(),
                ClientPackageId = clientPackageId,
                ServiceId = item.ServiceId,
                TotalEntries = package.EntryMode == PackageEntryMode.PerService ? item.EntryCount : null,
                RemainingEntries = package.EntryMode == PackageEntryMode.PerService ? item.EntryCount : null
            });
        }

        // T1-9: upis unatrag ide u povijest klijenta (tko, kada, PurchaseDate) u istom SaveChanges kao i paket. Upis paketa ne
        // dira postojeća sudjelovanja (nema retroaktivnog pokrića) i ne stvara proviziju (provizija na prodaju je samo checkout).
        ClientAuditLog audit = backdated
            ? new ClientAuditLog
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ClientId = clientId,
                ChangeType = ClientAuditChangeTypes.PackageIssuedBackdated,
                NewValue = $"clientPackageId={clientPackageId};purchaseDate={purchaseDate:yyyy-MM-dd}",
                ChangedAt = now,
                ChangedBy = userId
            }
            : null;
        await _clientPackageHandler.Add(clientPackage, audit);
        return await GetById(organizationId, clientId, clientPackageId);
    }

    /// <summary>T1-9 — dan kupnje ručnog upisa paketa: nikad nakon današnjeg dana (zona poslovnice prodaje, bez nje organizacije;
    /// poslovni sat); prije današnjeg dana samo uz clients.packages.write.past (bez granice unatrag, isti obrazac kao
    /// roster.entries.write.past). Vraća je li upis unatrag. Checkout uvijek prodaje na današnji dan i ne prolazi ovuda.</summary>
    private async Task<bool> EnsurePurchaseDateAllowed(Guid organizationId, Guid userId, DateOnly purchaseDate, DateOnly today)
    {
        if (purchaseDate > today)
            throw new ValidationAppException(ErrorCodes.PackagePurchaseDateInFuture,
                $"Datum kupnje paketa ({purchaseDate:dd.MM.yyyy.}) ne smije biti nakon današnjeg dana ({today:dd.MM.yyyy.}).");
        if (purchaseDate == today)
            return false;

        GrantContext grants = await _grantResolver.Resolve(organizationId, userId);
        if (!grants.Has(Grants.ClientsPackagesWritePast))
            throw ForbiddenAppException.MissingGrant(
                "Upis paketa s datumom kupnje prije današnjeg dana zahtijeva ovlast clients.packages.write.past.", Grants.ClientsPackagesWritePast);
        return true;
    }

    public async Task<ClientPackageDto> GetById(Guid organizationId, Guid clientId, Guid id)
    {
        ClientPackage clientPackage = await _clientPackageHandler.GetById(organizationId, id);
        if (clientPackage == null || clientPackage.ClientId != clientId)
            throw new NotFoundAppException("ClientPackage", id);

        return ToDto(clientPackage, await TodayForOrganization(organizationId));
    }

    public async Task<List<ClientPackageDto>> GetByClient(Guid organizationId, Guid clientId)
    {
        List<ClientPackage> packages = await _clientPackageHandler.GetByClient(organizationId, clientId);
        DateOnly today = await TodayForOrganization(organizationId);
        return packages.Select(cp => ToDto(cp, today)).ToList();
    }

    /// <remarks>Phase D3B3A.1: <paramref name="date"/> je trenutak izvođenja usluge; valjanost je usporedba njegovog
    /// lokalnog DATUMA u efektivnoj zoni poslovnice termina (<paramref name="companyId"/>, obavezno — paket se bira za
    /// uslugu koja se izvodi u poslovnici) s ClientPackage.ValidUntilDate — vidi PackageValidity.</remarks>
    public async Task<List<ClientPackageDto>> GetEligibleForService(
        Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date, Guid companyId)
    {
        Company company = await _companyHandler.GetById(organizationId, companyId);
        if (company == null)
            throw new NotFoundAppException("Company", companyId);

        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, companyId);
        List<ClientPackage> packages = await _clientPackageHandler.GetEligibleForService(
            organizationId, clientId, serviceId, PackageValidity.ServiceDate(calendar, date));
        DateOnly today = await TodayForOrganization(organizationId);
        return packages.Select(cp => ToDto(cp, today)).ToList();
    }

    public async Task<PackageConsumption> Consume(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, ParticipationExecutionContext execution,
        Guid clientPackageId, BookingStatus trigger)
    {
        PackageConsumptionTiming timing = await _organizationSettingsService.GetPackageConsumptionTiming(organizationId);
        if (!PackageConsumptionPolicy.ConsumesOn(timing, trigger))
            return null;

        return await ConsumeCore(uow, organizationId, userId, participation, execution, clientPackageId,
            PackageConsumptionTrigger.ServiceCompletion, consequenceId: null);
    }

    public async Task<PackageConsumption> ConsumeForPolicyConsequence(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, ParticipationExecutionContext execution,
        Guid clientPackageId, Guid consequenceId)
    {
        return await ConsumeCore(uow, organizationId, userId, participation, execution, clientPackageId,
            PackageConsumptionTrigger.PolicyConsequence, consequenceId);
    }

    private async Task<PackageConsumption> ConsumeCore(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, ParticipationExecutionContext execution,
        Guid clientPackageId, PackageConsumptionTrigger trigger, Guid? consequenceId)
    {
        ArgumentNullException.ThrowIfNull(participation);
        PackageConsumption active = PackageConsumptions.ActiveOf(participation);
        if (active != null)
        {
            if (active.ClientPackageId == clientPackageId)
                return active; // idempotentno: isto sudjelovanje se ne troši dvaput
            throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Sudjelovanje je već pokriveno drugim paketom.");
        }

        // Zaključaj paket PRIJE čitanja brojača — konkurentna potrošnja istog (zadnjeg) ulaska čeka i vidi svježe stanje.
        ClientPackage clientPackage = await _clientPackageHandler.GetForUpdate(uow, organizationId, clientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", clientPackageId);
        if (clientPackage.ClientId != execution.ClientId)
            throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Odabrani paket ne pripada klijentu ovog termina.");
        // Phase D3B3B: jedino pravilo isključivosti paket/novac — sudjelovanje s aktivnim novčanim namirenjem (bilo kojim
        // checkoutom) ne smije potrošiti paket. Sudjelovanje se zaključava kao i kod svakog novčanog namirenja, pa
        // konkurentno plaćanje i potrošnja paketa ne mogu oboje proći.
        await _participationHandler.LockForUpdate(uow, organizationId, new[] { participation.Id.GetValueOrDefault() });
        SettlementExclusivityPolicy.EnsurePackageAllowed(participation, ParticipationSettlement.SettledAmountOf(
            await _checkoutHandler.GetItemsForParticipation(uow, organizationId, participation.Id.GetValueOrDefault())));
        if (await _clientPackageHandler.HasActiveConsumption(uow, participation.Id.GetValueOrDefault()))
            throw new BusinessRuleException(ErrorCodes.ConcurrencyConflict,
                "Sudjelovanje je upravo pokriveno paketom od strane drugog zahtjeva — pokušajte ponovno.");

        // F-08: valjanost na DATUM IZVOĐENJA usluge (lokalni datum poslovnice), ne na trenutni sat.
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, execution.CompanyId);
        bool counted = PackageCounting.IsCounted(clientPackage, execution.ServiceId);
        // P1 (D6): kazna politike troši isključivo jedinicu BROJENOG paketa — neograničen paket nikad nije izvor kazne.
        if (trigger == PackageConsumptionTrigger.PolicyConsequence && !counted)
            throw new BusinessRuleException(ErrorCodes.PackageNotEligible, "Neograničen paket ne može podmiriti kaznu politike otkazivanja.");
        DateOnly serviceDate = PackageValidity.ServiceDate(calendar, execution.StartsAt);
        ClientPackageEntryMutator.Deduct(clientPackage, execution.ServiceId, serviceDate);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        clientPackage.UpdatedAt = now;
        clientPackage.UpdatedBy = userId;
        await _clientPackageHandler.Update(uow, clientPackage);

        PackageConsumption consumption = new PackageConsumption
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ClientPackageId = clientPackageId,
            BookingSegmentParticipationId = participation.Id.GetValueOrDefault(),
            ServiceId = execution.ServiceId,
            Units = counted ? 1 : 0,
            ServiceStartsAt = execution.StartsAt,
            ServiceDate = serviceDate,
            Status = PackageConsumptionStatus.Consumed,
            Trigger = trigger,
            ParticipationPolicyConsequenceId = consequenceId,
            CreatedAt = now,
            CreatedBy = userId
        };
        if (!participation.PackageConsumptions.Contains(consumption))
            participation.PackageConsumptions.Add(consumption);
        await _clientPackageHandler.AddConsumption(uow, consumption);
        return consumption;
    }

    public async Task<bool> ReverseActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, BookingSegmentParticipation participation, PackageConsumptionReversalReason reason)
    {
        PackageConsumption active = PackageConsumptions.ActiveOf(participation);
        if (active == null)
            return false;

        ClientPackage clientPackage = await _clientPackageHandler.GetForUpdate(uow, organizationId, active.ClientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", active.ClientPackageId);

        // Pod lockom paketa: potrošnja je možda već poništena konkurentnim zahtjevom — tada ništa ne vraćamo (nikad dvaput).
        if (!await _clientPackageHandler.TryMarkReversed(uow, active, userId, reason, _timeProvider.GetUtcNow()))
            return false;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        ClientPackageEntryMutator.Return(clientPackage, active.ServiceId);
        clientPackage.UpdatedAt = now;
        clientPackage.UpdatedBy = userId;
        await _clientPackageHandler.Update(uow, clientPackage);
        return true;
    }

    /// <summary>Active/Depleted/Expired -> Cancelled. Terminalno (nema "uncancel") — vidi ClientPackageEntryMutator.Return,
    /// koje namjerno nikad ne mijenja Cancelled natrag u Active.</summary>
    public async Task<ClientPackageDto> Cancel(Guid organizationId, Guid clientId, Guid id, Guid userId)
    {
        await MutateWithConcurrencyRetry(organizationId, id, userId, clientPackage =>
        {
            if (clientPackage.ClientId != clientId)
                throw new NotFoundAppException("ClientPackage", id);
            if (clientPackage.Status == ClientPackageStatus.Cancelled)
                throw new BusinessRuleException(ErrorCodes.PackageAlreadyCancelled, "Paket je već otkazan.");

            clientPackage.Status = ClientPackageStatus.Cancelled;
            clientPackage.CancelledAt = _timeProvider.GetUtcNow();
            clientPackage.CancelledBy = userId;
        });

        return await GetById(organizationId, clientId, id);
    }

    /// <summary>Čita paket, primjenjuje `mutate` i sprema — uz retry na optimistic-concurrency sudar (xmin token,
    /// vidi DatabaseContext). Bez ovoga bi dva paralelna check-ina na isti paket (npr. dva klijenta na istom
    /// grupnom terminu) mogla izgubiti jedan od dva dekrementa (lost update).</summary>
    private async Task MutateWithConcurrencyRetry(
        Guid organizationId, Guid clientPackageId, Guid userId, Action<ClientPackage> mutate)
    {
        for (int attempt = 1; ; attempt++)
        {
            ClientPackage clientPackage = await _clientPackageHandler.GetById(organizationId, clientPackageId);
            if (clientPackage == null)
                throw new NotFoundAppException("ClientPackage", clientPackageId);

            mutate(clientPackage);

            clientPackage.UpdatedAt = _timeProvider.GetUtcNow();
            clientPackage.UpdatedBy = userId;

            try
            {
                await _clientPackageHandler.Update(clientPackage);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyRetries)
            {
                // netko drugi je paralelno promijenio isti paket — ponovi s najsvježijim stanjem
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    ErrorCodes.ConcurrencyConflict,
                    "Paket je upravo promijenjen od strane drugog zahtjeva — pokušajte ponovno.");
            }
        }
    }

    private async Task<decimal> ResolveSuggestedPrice(Guid organizationId, Guid packageId, Guid? companyId, DateOnly date)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Package,
            SubjectId = packageId,
            CompanyId = companyId,
            Date = date
        });
        return resolved.Price;
    }

    /// <summary>Današnji kalendarski datum u kalendaru organizacije — samo za PRIKAZ efektivnog statusa (Expired) paketa,
    /// koji nije vezan uz poslovnicu; potrošnja/eligibility uvijek koriste datum izvođenja usluge.</summary>
    private async Task<DateOnly> TodayForOrganization(Guid organizationId) =>
        (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(_timeProvider.GetUtcNow());

    private static ClientPackageDto ToDto(ClientPackage cp, DateOnly today)
    {
        return new ClientPackageDto
        {
            Id = cp.Id.GetValueOrDefault(),
            ClientId = cp.ClientId,
            PackageId = cp.PackageId,
            PackageName = cp.Package?.Name,
            PurchaseDate = cp.PurchaseDate,
            PaidPrice = cp.PaidPrice,
            EntryMode = cp.EntryMode,
            TotalEntryCount = cp.TotalEntryCount,
            RemainingSharedEntries = cp.RemainingSharedEntries,
            ValidityType = cp.ValidityType,
            ValidUntilDate = cp.ValidUntilDate,
            Status = ClientPackageStatusResolver.GetEffectiveStatus(cp, today),
            ServiceEntries = cp.ServiceEntries.Select(e => new ClientPackageServiceEntryDto
            {
                ServiceId = e.ServiceId,
                ServiceName = e.Service?.Name,
                TotalEntries = e.TotalEntries,
                RemainingEntries = e.RemainingEntries
            }).ToList(),
            CreatedAt = cp.CreatedAt,
            CreatedBy = cp.CreatedBy,
            UpdatedAt = cp.UpdatedAt,
            UpdatedBy = cp.UpdatedBy,
            CancelledAt = cp.CancelledAt,
            CancelledBy = cp.CancelledBy
        };
    }
}
