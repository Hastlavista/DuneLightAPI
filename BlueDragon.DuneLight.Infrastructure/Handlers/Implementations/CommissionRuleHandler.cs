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

    public async Task<List<CommissionRule>> GetList(Guid organizationId, Guid? employeeId, CommissionSubjectType? subjectType, bool? isActive)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);

        IQueryable<CommissionRule> query = context.CommissionRules
            .Include(r => r.Employee)
            .Include(r => r.Service)
            .Include(r => r.Product)
            .Include(r => r.Package)
            .Where(r => r.OrganizationId == organizationId);

        if (employeeId.HasValue)
            query = query.Where(r => r.EmployeeId == employeeId.Value);
        if (subjectType.HasValue)
            query = query.Where(r => r.SubjectType == subjectType.Value);
        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        return await query
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<CommissionRule> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionRules
            .Include(r => r.Employee)
            .Include(r => r.Service)
            .Include(r => r.Product)
            .Include(r => r.Package)
            .SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == id);
    }

    public async Task<CommissionRule> GetActiveForSubject(
        Guid organizationId, Guid employeeId, CommissionSubjectType subjectType, Guid? serviceId, Guid? productId, Guid? packageId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionRules.SingleOrDefaultAsync(r =>
            r.OrganizationId == organizationId &&
            r.EmployeeId == employeeId &&
            r.SubjectType == subjectType &&
            r.ServiceId == serviceId &&
            r.ProductId == productId &&
            r.PackageId == packageId &&
            r.IsActive);
    }

    public async Task<List<CommissionRule>> GetAllActiveForEmployee(Guid organizationId, Guid employeeId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CommissionRules
            .Where(r => r.OrganizationId == organizationId && r.EmployeeId == employeeId && r.IsActive)
            .ToListAsync();
    }

    public async Task Add(CommissionRule rule)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CommissionRules.Add(rule);
        await context.SaveChangesAsync();
    }

    public async Task Update(CommissionRule rule)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CommissionRules.Update(rule);
        await context.SaveChangesAsync();
    }

    public async Task Delete(CommissionRule rule)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.CommissionRules.Remove(rule);
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
}
