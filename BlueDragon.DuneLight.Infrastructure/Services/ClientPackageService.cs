using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Catalog;
using BlueDragon.DuneLight.Core.DTOs.Clients;
using BlueDragon.DuneLight.Core.Enums;
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

    public ClientPackageService(
        IClientPackageHandler clientPackageHandler,
        IClientHandler clientHandler,
        IPackageHandler packageHandler,
        ICompanyHandler companyHandler,
        IPricingService pricingService,
        IOrganizationCalendarService organizationCalendarService,
        IOrganizationSettingsService organizationSettingsService)
    {
        _organizationCalendarService = organizationCalendarService;
        _organizationSettingsService = organizationSettingsService;
        _clientPackageHandler = clientPackageHandler;
        _clientHandler = clientHandler;
        _packageHandler = packageHandler;
        _companyHandler = companyHandler;
        _pricingService = pricingService;
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

        DateTimeOffset purchaseDate = request.PurchaseDate ?? DateTimeOffset.UtcNow;

        decimal paidPrice = request.PaidPrice ?? await ResolveSuggestedPrice(organizationId, request.PackageId, request.CompanyId, purchaseDate);

        // Phase D3B3A.1: poslovni datum kupnje = lokalni datum u kalendaru poslovnice prodaje (organizacije kad prodaja
        // stvarno nema poslovnicu) — ne offset ulazne PurchaseDate vrijednosti, ne UTC datum, ne zona hosta.
        OrganizationCalendar organizationCalendar = await _organizationCalendarService.GetCalendar(organizationId);
        OrganizationCalendar saleCalendar = request.CompanyId.HasValue
            ? await _organizationCalendarService.GetCompanyCalendar(organizationId, request.CompanyId.Value)
            : organizationCalendar;
        DateOnly validUntilDate = PackageExpiryCalculator.ForSale(package, purchaseDate, saleCalendar, organizationCalendar);

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
            CreatedAt = DateTimeOffset.UtcNow,
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

        await _clientPackageHandler.Add(clientPackage);
        return await GetById(organizationId, clientId, clientPackageId);
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
        IUnitOfWork uow, Guid organizationId, Guid userId, Booking booking, BookingExecutionContext execution,
        Guid clientPackageId, BookingStatus trigger)
    {
        PackageConsumptionTiming timing = await _organizationSettingsService.GetPackageConsumptionTiming(organizationId);
        if (!PackageConsumptionPolicy.ConsumesOn(timing, trigger))
            return null;

        BookingSegmentParticipation participation = BookingParticipations.GetSingleParticipation(booking);
        PackageConsumption active = PackageConsumptions.ActiveOf(booking);
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
        if (await _clientPackageHandler.HasActiveConsumption(uow, participation.Id.GetValueOrDefault()))
            throw new BusinessRuleException(ErrorCodes.ConcurrencyConflict,
                "Sudjelovanje je upravo pokriveno paketom od strane drugog zahtjeva — pokušajte ponovno.");

        // F-08: valjanost na DATUM IZVOĐENJA usluge (lokalni datum poslovnice), ne na trenutni sat.
        OrganizationCalendar calendar = await _organizationCalendarService.GetCompanyCalendar(organizationId, execution.CompanyId);
        bool counted = IsCounted(clientPackage, execution.ServiceId);
        DateOnly serviceDate = PackageValidity.ServiceDate(calendar, execution.StartsAt);
        ClientPackageEntryMutator.Deduct(clientPackage, execution.ServiceId, serviceDate);

        DateTimeOffset now = DateTimeOffset.UtcNow;
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
            CreatedAt = now,
            CreatedBy = userId
        };
        if (!participation.PackageConsumptions.Contains(consumption))
            participation.PackageConsumptions.Add(consumption);
        await _clientPackageHandler.AddConsumption(uow, consumption);
        return consumption;
    }

    public async Task<bool> ReverseActive(
        IUnitOfWork uow, Guid organizationId, Guid userId, Booking booking, PackageConsumptionReversalReason reason)
    {
        PackageConsumption active = PackageConsumptions.ActiveOf(booking);
        if (active == null)
            return false;

        ClientPackage clientPackage = await _clientPackageHandler.GetForUpdate(uow, organizationId, active.ClientPackageId);
        if (clientPackage == null)
            throw new NotFoundAppException("ClientPackage", active.ClientPackageId);

        // Pod lockom paketa: potrošnja je možda već poništena konkurentnim zahtjevom — tada ništa ne vraćamo (nikad dvaput).
        if (!await _clientPackageHandler.TryMarkReversed(uow, active, userId, reason, DateTimeOffset.UtcNow))
            return false;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ClientPackageEntryMutator.Return(clientPackage, active.ServiceId);
        clientPackage.UpdatedAt = now;
        clientPackage.UpdatedBy = userId;
        await _clientPackageHandler.Update(uow, clientPackage);
        return true;
    }

    /// <summary>Ima li paket brojač za ovu uslugu (potrošnja skida 1 jedinicu) ili je neograničen (0 jedinica).</summary>
    private static bool IsCounted(ClientPackage clientPackage, Guid serviceId)
    {
        if (clientPackage.EntryMode == PackageEntryMode.SharedPool)
            return clientPackage.RemainingSharedEntries.HasValue;

        ClientPackageServiceEntry entry = clientPackage.ServiceEntries.FirstOrDefault(e => e.ServiceId == serviceId);
        return entry?.RemainingEntries != null;
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
            clientPackage.CancelledAt = DateTimeOffset.UtcNow;
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

            clientPackage.UpdatedAt = DateTimeOffset.UtcNow;
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

    private async Task<decimal> ResolveSuggestedPrice(Guid organizationId, Guid packageId, Guid? companyId, DateTimeOffset date)
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
        (await _organizationCalendarService.GetCalendar(organizationId)).LocalDate(DateTimeOffset.UtcNow);

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
