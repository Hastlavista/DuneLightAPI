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
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class ClientPackageService : IClientPackageService
{
    private const int MaxConcurrencyRetries = 3;

    private readonly IClientPackageHandler _clientPackageHandler;
    private readonly IClientHandler _clientHandler;
    private readonly IPackageHandler _packageHandler;
    private readonly IPricingService _pricingService;

    public ClientPackageService(
        IClientPackageHandler clientPackageHandler,
        IClientHandler clientHandler,
        IPackageHandler packageHandler,
        IPricingService pricingService)
    {
        _clientPackageHandler = clientPackageHandler;
        _clientHandler = clientHandler;
        _packageHandler = packageHandler;
        _pricingService = pricingService;
    }

    public async Task<ClientPackageDto> Create(Guid organizationId, Guid userId, Guid clientId, ClientPackageCreateRequest request)
    {
        Client client = await _clientHandler.GetByIdLight(organizationId, clientId);
        if (client == null)
            throw new NotFoundAppException("Client", clientId);

        Package package = await _packageHandler.GetById(organizationId, request.PackageId);
        if (package == null)
            throw new NotFoundAppException("Package", request.PackageId);

        DateTimeOffset purchaseDate = request.PurchaseDate ?? DateTimeOffset.UtcNow;

        decimal paidPrice = request.PaidPrice ?? await ResolveSuggestedPrice(organizationId, request.PackageId, request.LocationId, purchaseDate);

        DateTimeOffset expiryDate = PackageExpiryCalculator.CalculateExpiryDate(
            package.ValidityType, purchaseDate, package.ValidityDays, package.ValidityFixedDate);

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
            ExpiryDate = expiryDate,
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

        return ToDto(clientPackage);
    }

    public async Task<List<ClientPackageDto>> GetByClient(Guid organizationId, Guid clientId)
    {
        List<ClientPackage> packages = await _clientPackageHandler.GetByClient(organizationId, clientId);
        return packages.Select(ToDto).ToList();
    }

    public async Task<List<ClientPackageDto>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateTimeOffset date)
    {
        List<ClientPackage> packages = await _clientPackageHandler.GetEligibleForService(organizationId, clientId, serviceId, date);
        return packages.Select(ToDto).ToList();
    }

    public Task DeductEntry(Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId)
    {
        return MutateWithConcurrencyRetry(organizationId, clientPackageId, userId,
            clientPackage => ClientPackageEntryMutator.Deduct(clientPackage, serviceId));
    }

    public Task ReturnEntry(Guid organizationId, Guid clientPackageId, Guid serviceId, Guid userId)
    {
        return MutateWithConcurrencyRetry(organizationId, clientPackageId, userId,
            clientPackage => ClientPackageEntryMutator.Return(clientPackage, serviceId));
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

    private async Task<decimal> ResolveSuggestedPrice(Guid organizationId, Guid packageId, Guid? locationId, DateTimeOffset date)
    {
        ResolvePriceResponse resolved = await _pricingService.ResolvePrice(organizationId, new ResolvePriceRequest
        {
            SubjectType = PricingSubjectType.Package,
            SubjectId = packageId,
            LocationId = locationId,
            Date = date
        });
        return resolved.Price;
    }

    private static ClientPackageDto ToDto(ClientPackage cp)
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
            ExpiryDate = cp.ExpiryDate,
            Status = cp.Status,
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
            UpdatedBy = cp.UpdatedBy
        };
    }
}
