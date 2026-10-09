using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Clients;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class ClientPackageHandler : IClientPackageHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientPackageHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task Add(ClientPackage clientPackage, ClientAuditLog audit = null)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientPackages.Add(clientPackage);
        if (audit != null)
            context.ClientAuditLogs.Add(audit);
        await context.SaveChangesAsync();
    }

    public async Task<ClientPackage> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await GetByIdCore(context, organizationId, id);
    }

    public Task<ClientPackage> GetById(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return GetByIdCore(uow.Context, organizationId, id);
    }

    public async Task<ClientPackage> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.client_packages WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE");
        ClientPackage clientPackage = await GetByIdCore(uow.Context, organizationId, id);
        // Već praćen entitet (učitan ranije u istoj transakciji) bi bio stariji od zaključanog retka — osvježi ga.
        if (clientPackage != null)
        {
            await uow.Context.Entry(clientPackage).ReloadAsync();
            foreach (ClientPackageServiceEntry entry in clientPackage.ServiceEntries)
                await uow.Context.Entry(entry).ReloadAsync();
        }

        return clientPackage;
    }

    public async Task AddConsumption(IUnitOfWork uow, PackageConsumption consumption)
    {
        uow.Context.PackageConsumptions.Add(consumption);
        await uow.Context.SaveChangesAsync();
    }

    public Task<bool> HasActiveConsumption(IUnitOfWork uow, Guid participationId)
    {
        return uow.Context.PackageConsumptions.AnyAsync(c =>
            c.BookingSegmentParticipationId == participationId && c.Status == PackageConsumptionStatus.Consumed);
    }

    public async Task<bool> TryMarkReversed(
        IUnitOfWork uow, PackageConsumption consumption, Guid userId, PackageConsumptionReversalReason reason, DateTimeOffset at)
    {
        // Uvjetni UPDATE (WHERE status = 'Consumed') — atomarno i neovisno o (možda zastarjelom) praćenom entitetu.
        int updated = await uow.Context.PackageConsumptions
            .Where(c => c.Id == consumption.Id && c.Status == PackageConsumptionStatus.Consumed)
            .ExecuteUpdateAsync(x => x
                .SetProperty(c => c.Status, PackageConsumptionStatus.Reversed)
                .SetProperty(c => c.ReversedAt, at)
                .SetProperty(c => c.ReversedBy, userId)
                .SetProperty(c => c.ReversalReason, reason));
        if (updated == 0)
            return false;

        // Uskladi praćeni entitet s bazom (bez ponovnog pisanja).
        consumption.Status = PackageConsumptionStatus.Reversed;
        consumption.ReversedAt = at;
        consumption.ReversedBy = userId;
        consumption.ReversalReason = reason;
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<PackageConsumption> entry = uow.Context.Entry(consumption);
        if (entry.State != EntityState.Detached)
        {
            entry.OriginalValues.SetValues(entry.CurrentValues);
            entry.State = EntityState.Unchanged;
        }
        return true;
    }

    private static Task<ClientPackage> GetByIdCore(DatabaseContext context, Guid organizationId, Guid id)
    {
        return context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .SingleOrDefaultAsync(cp => cp.OrganizationId == organizationId && cp.Id == id);
    }

    public async Task<List<ClientPackage>> GetByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .Where(cp => cp.OrganizationId == organizationId && cp.ClientId == clientId)
            .OrderByDescending(cp => cp.PurchaseDate)
            .ThenByDescending(cp => cp.CreatedAt) // T1-7: PurchaseDate je dan — isti dan razlikuje trenutak upisa
            .ToListAsync();
    }

    public async Task<List<ClientPackage>> GetEligibleForService(Guid organizationId, Guid clientId, Guid serviceId, DateOnly serviceDate)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages
            .Include(cp => cp.Package)
            .Include(cp => cp.ServiceEntries).ThenInclude(e => e.Service)
            .Where(cp =>
                cp.OrganizationId == organizationId &&
                cp.ClientId == clientId &&
                cp.Status == ClientPackageStatus.Active &&
                cp.PurchaseDate <= serviceDate && cp.ValidUntilDate >= serviceDate && // PackageValidity.IsValidOn (usporedba datuma, T1-9: od PurchaseDate), izraženo u SQL-u
                cp.ServiceEntries.Any(se => se.ServiceId == serviceId) &&
                (
                    (cp.EntryMode == PackageEntryMode.SharedPool && (cp.RemainingSharedEntries == null || cp.RemainingSharedEntries > 0)) ||
                    (cp.EntryMode == PackageEntryMode.PerService && cp.ServiceEntries.Any(se =>
                        se.ServiceId == serviceId && (se.RemainingEntries == null || se.RemainingEntries > 0)))
                ))
            .OrderBy(cp => cp.ValidUntilDate)
            .ToListAsync();
    }

    public async Task Update(ClientPackage clientPackage)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.ClientPackages.Update(clientPackage);
        await context.SaveChangesAsync();
    }

    public async Task Update(IUnitOfWork uow, ClientPackage clientPackage)
    {
        uow.Context.ClientPackages.Update(clientPackage);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<bool> HasAnyForClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages.AnyAsync(cp => cp.OrganizationId == organizationId && cp.ClientId == clientId);
    }

    /// <summary>
    /// Ekvivalent ClientPackageStatusResolver.GetEffectiveStatus(cp, now) == Active, izražen kao upit umjesto
    /// učitavanja punih entiteta. Persistirani Status prima samo Active/Depleted/Cancelled (Expired se nikad
    /// ne perzistira — vidi ClientPackageStatus.cs), pa je "efektivno Active" logički točno
    /// Status == Active && ValidUntilDate >= today (Cancelled/Depleted su isključeni samim Status == Active uvjetom,
    /// Expired samim datumskim uvjetom). Ako se presedan Resolvera ikad promijeni, uskladiti i ovdje.
    /// </summary>
    public async Task<bool> HasUsableForClient(Guid organizationId, Guid clientId, DateOnly today)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientPackages.AnyAsync(cp =>
            cp.OrganizationId == organizationId &&
            cp.ClientId == clientId &&
            cp.Status == ClientPackageStatus.Active &&
            cp.ValidUntilDate >= today);
    }
}
