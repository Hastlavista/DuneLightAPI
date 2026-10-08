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

public class CancellationPolicyHandler : ICancellationPolicyHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CancellationPolicyHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<CancellationPolicy>> GetAll(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationPolicies
            .AsNoTracking()
            .Include(p => p.Versions)
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.IsOrganizationDefault).ThenBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<CancellationPolicy> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationPolicies
            .AsNoTracking()
            .Include(p => p.Versions)
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
    }

    public async Task<CancellationPolicy> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.cancellation_policies WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE");
        CancellationPolicy policy = await uow.Context.CancellationPolicies
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == id);
        if (policy != null)
            await uow.Context.Entry(policy).ReloadAsync();
        return policy;
    }

    public async Task<CancellationPolicy> GetOrganizationDefaultForUpdate(IUnitOfWork uow, Guid organizationId)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.cancellation_policies WHERE organization_id = {organizationId} AND is_organization_default FOR UPDATE");
        CancellationPolicy policy = await uow.Context.CancellationPolicies
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.IsOrganizationDefault);
        if (policy != null)
            await uow.Context.Entry(policy).ReloadAsync();
        return policy;
    }

    public async Task<int> GetLatestVersionNumber(IUnitOfWork uow, Guid policyId)
    {
        return await uow.Context.CancellationPolicyVersions
            .Where(v => v.CancellationPolicyId == policyId)
            .Select(v => (int?)v.Version)
            .MaxAsync() ?? 0;
    }

    public Task<bool> HasAssignments(IUnitOfWork uow, Guid organizationId, Guid policyId)
    {
        return uow.Context.CancellationPolicyAssignments
            .AnyAsync(a => a.OrganizationId == organizationId && a.CancellationPolicyId == policyId);
    }

    public Task<bool> ActiveNameExists(IUnitOfWork uow, Guid organizationId, string name, Guid? exceptId)
    {
        string normalized = name.Trim().ToLower();
        return uow.Context.CancellationPolicies.AnyAsync(p =>
            p.OrganizationId == organizationId && p.IsActive && p.Name.ToLower() == normalized && p.Id != exceptId);
    }

    public async Task<List<CancellationPolicyAssignment>> GetAssignments(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CancellationPolicyAssignments
            .AsNoTracking()
            .Include(a => a.Policy)
            .Include(a => a.Company)
            .Include(a => a.Service)
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.Company.Name).ThenBy(a => a.Service.Name)
            .ToListAsync();
    }

    public Task<CancellationPolicyAssignment> GetAssignmentForScope(IUnitOfWork uow, Guid organizationId, Guid? companyId, Guid? serviceId)
    {
        return uow.Context.CancellationPolicyAssignments.SingleOrDefaultAsync(a =>
            a.OrganizationId == organizationId && a.CompanyId == companyId && a.ServiceId == serviceId);
    }

    public Task<CancellationPolicyAssignment> GetAssignment(IUnitOfWork uow, Guid organizationId, Guid assignmentId)
    {
        return uow.Context.CancellationPolicyAssignments
            .SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == assignmentId);
    }

    public Task<List<CancellationPolicyAssignment>> GetResolutionCandidates(IUnitOfWork uow, Guid organizationId, Guid companyId, Guid serviceId)
    {
        return uow.Context.CancellationPolicyAssignments
            .AsNoTracking()
            .Where(a => a.OrganizationId == organizationId &&
                        ((a.CompanyId == companyId && a.ServiceId == serviceId) ||
                         (a.CompanyId == null && a.ServiceId == serviceId) ||
                         (a.CompanyId == companyId && a.ServiceId == null)))
            .ToListAsync();
    }

    public Task<CancellationPolicy> GetOrganizationDefault(IUnitOfWork uow, Guid organizationId)
    {
        return uow.Context.CancellationPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.IsOrganizationDefault);
    }

    public Task<CancellationPolicyVersion> GetLatestVersion(IUnitOfWork uow, Guid policyId)
    {
        return uow.Context.CancellationPolicyVersions
            .AsNoTracking()
            .Include(v => v.Policy)
            .Where(v => v.CancellationPolicyId == policyId)
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync();
    }
}
