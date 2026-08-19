using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Roster;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class LeaveFundHandler : ILeaveFundHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public LeaveFundHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<LeaveFund>> GetForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.LeaveFunds
            .Where(f => f.OrganizationId == organizationId && f.EmployeeId == employeeId)
            .OrderBy(f => f.FundYear)
            .ToListAsync();
    }

    public async Task<LeaveFund> GetOrCreateForYear(
        IUnitOfWork uow, Guid organizationId, Guid employeeId, EmployeeLeaveSettings settings, int fundYear, Guid userId)
    {
        LeaveFund existing = await uow.Context.LeaveFunds
            .SingleOrDefaultAsync(f => f.OrganizationId == organizationId && f.EmployeeId == employeeId && f.FundYear == fundYear);

        if (existing != null)
            return existing;

        LeaveFund fund = new LeaveFund
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EmployeeId = employeeId,
            FundYear = fundYear,
            OpenedAt = LeaveFundYearCalculator.ResolveOpenedAt(settings, fundYear),
            ExpiresAt = LeaveFundYearCalculator.ResolveExpiresAt(settings, fundYear),
            AllocatedDays = settings.AnnualDays,
            UsedDays = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = userId
        };

        uow.Context.LeaveFunds.Add(fund);
        await uow.Context.SaveChangesAsync();
        return fund;
    }

    public async Task<List<LeaveFund>> GetEligible(IUnitOfWork uow, Guid organizationId, Guid employeeId, DateTimeOffset asOf)
    {
        return await uow.Context.LeaveFunds
            .Where(f =>
                f.OrganizationId == organizationId &&
                f.EmployeeId == employeeId &&
                f.ExpiresAt >= asOf &&
                f.AllocatedDays > f.UsedDays)
            .OrderBy(f => f.FundYear)
            .ToListAsync();
    }

    public async Task Update(IUnitOfWork uow, LeaveFund fund)
    {
        uow.Context.LeaveFunds.Update(fund);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<List<LeaveFundUsage>> GetUsagesForEntry(IUnitOfWork uow, Guid organizationId, Guid rosterEntryId)
    {
        return await uow.Context.LeaveFundUsages
            .Include(u => u.LeaveFund)
            .Where(u => u.OrganizationId == organizationId && u.RosterEntryId == rosterEntryId)
            .ToListAsync();
    }

    public async Task AddUsage(IUnitOfWork uow, LeaveFundUsage usage)
    {
        uow.Context.LeaveFundUsages.Add(usage);
        await uow.Context.SaveChangesAsync();
    }

    public async Task DeleteUsages(IUnitOfWork uow, List<LeaveFundUsage> usages)
    {
        uow.Context.LeaveFundUsages.RemoveRange(usages);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<LeaveFund> ManualUpsert(
        Guid organizationId, Guid employeeId, EmployeeLeaveSettings settings, int fundYear, int allocatedDays, Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        LeaveFund existing = await context.LeaveFunds
            .SingleOrDefaultAsync(f => f.OrganizationId == organizationId && f.EmployeeId == employeeId && f.FundYear == fundYear);

        if (existing == null)
        {
            existing = new LeaveFund
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                EmployeeId = employeeId,
                FundYear = fundYear,
                OpenedAt = LeaveFundYearCalculator.ResolveOpenedAt(settings, fundYear),
                ExpiresAt = LeaveFundYearCalculator.ResolveExpiresAt(settings, fundYear),
                AllocatedDays = allocatedDays,
                UsedDays = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };
            context.LeaveFunds.Add(existing);
        }
        else
        {
            existing.AllocatedDays = allocatedDays;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedBy = userId;
        }

        await context.SaveChangesAsync();
        return existing;
    }
}
