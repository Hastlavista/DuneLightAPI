using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Catalog;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class MembershipPlanHandler : IMembershipPlanHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public MembershipPlanHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<MembershipPlan>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithVersionGraph(context.MembershipPlans)
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<MembershipPlan> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithVersionGraph(context.MembershipPlans)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task<MembershipPlan> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.membership_plans WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE");
        MembershipPlan plan = await uow.Context.MembershipPlans
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
        if (plan != null)
            await uow.Context.Entry(plan).ReloadAsync();
        return plan;
    }

    public Task<MembershipPlanVersion> GetLatestVersion(IUnitOfWork uow, Guid planId)
    {
        return uow.Context.MembershipPlanVersions
            .AsNoTracking()
            .Include(v => v.Services)
            .Include(v => v.Companies)
            .Include(v => v.UsageLimits)
            .Include(v => v.PriceBenefits)
            .Where(v => v.MembershipPlanId == planId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync();
    }

    public Task<bool> ActiveNameExists(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId)
    {
        string normalized = name.Trim().ToLower();
        return uow.Context.MembershipPlans.AnyAsync(p =>
            p.OrganizationId == organizationId && p.IsActive && p.Name.Trim().ToLower() == normalized && p.Id != exceptId);
    }

    private static IQueryable<MembershipPlan> WithVersionGraph(IQueryable<MembershipPlan> plans) => plans
        .AsNoTracking()
        .AsSplitQuery()
        .Include(p => p.Versions).ThenInclude(v => v.Services).ThenInclude(s => s.Service)
        .Include(p => p.Versions).ThenInclude(v => v.Companies).ThenInclude(c => c.Company)
        .Include(p => p.Versions).ThenInclude(v => v.UsageLimits).ThenInclude(l => l.Service)
        .Include(p => p.Versions).ThenInclude(v => v.PriceBenefits).ThenInclude(b => b.Service);
}
