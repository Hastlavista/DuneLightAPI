using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Commissions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CommissionRuleHandler : ICommissionRuleHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CommissionRuleHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<CommissionRule>> GetList(
        Guid organizationId, Guid? employeeId, CommissionRuleKind? kind, CommissionSubjectType? subjectType, bool? isActive)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<CommissionRule> query = WithDetails(context.CommissionRules)
            .Where(r => r.OrganizationId == organizationId);

        if (employeeId.HasValue)
            query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (kind.HasValue)
            query = query.Where(r => r.Kind == kind.Value);
        if (subjectType.HasValue)
            query = query.Where(r => r.SubjectType == subjectType.Value);
        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        return await query
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.EffectiveFrom)
            .ToListAsync();
    }

    public async Task<CommissionRule> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithDetails(context.CommissionRules)
            .SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task<CommissionRule> GetVersionOn(
        Guid organizationId, Guid employeeId, CommissionRuleKind kind, CommissionSubjectType subjectType, Guid? subjectId, DateOnly date)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        IQueryable<CommissionRule> query = context.CommissionRules
            .Include(r => r.Tiers)
            .Where(r => r.OrganizationId == organizationId && r.EmployeeId == employeeId && r.Kind == kind &&
                        r.SubjectType == subjectType && r.EffectiveFrom <= date);
        query = subjectType switch
        {
            CommissionSubjectType.Service => query.Where(r => r.ServiceId == subjectId),
            CommissionSubjectType.Product => query.Where(r => r.ProductId == subjectId),
            CommissionSubjectType.Package => query.Where(r => r.PackageId == subjectId),
            CommissionSubjectType.MembershipPlan => query.Where(r => r.MembershipPlanId == subjectId),
            CommissionSubjectType.AllServices => query,
            _ => throw new ArgumentOutOfRangeException(nameof(subjectType))
        };
        return await query.OrderByDescending(r => r.EffectiveFrom).FirstOrDefaultAsync();
    }

    public async Task Add(CommissionRule rule)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CommissionRules.Add(rule);
        await context.SaveChangesAsync();
    }

    public async Task Update(CommissionRule rule, List<CommissionRuleTier> tiers = null)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        await using var transaction = await context.Database.BeginTransactionAsync();
        CommissionRule tracked = await context.CommissionRules
            .Include(r => r.Tiers)
            .SingleAsync(r => r.Id == rule.Id);
        context.Entry(tracked).CurrentValues.SetValues(rule);
        if (tiers != null)
        {
            context.CommissionRuleTiers.RemoveRange(tracked.Tiers);
            await context.SaveChangesAsync();
            context.CommissionRuleTiers.AddRange(tiers);
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task Delete(CommissionRule rule)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        CommissionRule tracked = await context.CommissionRules.SingleAsync(r => r.Id == rule.Id);
        context.CommissionRules.Remove(tracked);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsReferenced(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionEntries.AnyAsync(e => e.OrganizationId == organizationId && e.CommissionRuleId == id);
    }

    public async Task<bool> HasActivePercentageRuleForService(Guid organizationId, Guid serviceId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionRules.AnyAsync(r =>
            r.OrganizationId == organizationId &&
            r.ServiceId == serviceId &&
            r.SubjectType == CommissionSubjectType.Service &&
            r.CalculationType == CommissionCalculationType.Percentage &&
            r.IsActive);
    }

    private static IQueryable<CommissionRule> WithDetails(IQueryable<CommissionRule> rules) => rules
        .Include(r => r.Employee)
        .Include(r => r.Service)
        .Include(r => r.Product)
        .Include(r => r.Package)
        .Include(r => r.MembershipPlan)
        .Include(r => r.Tiers);
}
