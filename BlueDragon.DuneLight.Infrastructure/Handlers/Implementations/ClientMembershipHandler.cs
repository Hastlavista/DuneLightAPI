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

public class ClientMembershipHandler : IClientMembershipHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public ClientMembershipHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<List<ClientMembership>> GetByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithGraph(context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.ClientId == clientId)
            .OrderByDescending(m => m.StartsOn).ThenByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<ClientMembership> GetById(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithGraph(context.ClientMemberships.AsNoTracking())
            .SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.Id == id);
    }

    public async Task<List<ClientMembership>> GetPlanUpdateNotApplied(Guid organizationId, Guid? planId, DateOnly today)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithGraph(context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.PlanUpdateSkippedVersionId != null && m.VoidedAt == null
                        && (m.EndsOn == null || m.EndsOn >= today) && (planId == null || m.MembershipPlanId == planId))
            .OrderBy(m => m.PlanUpdateSkippedAt)
            .ToListAsync();
    }

    public async Task<List<ClientMembership>> GetStandingStill(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithGraph(context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.VoidedAt == null
                        && m.Pauses.Any(p => p.Source == MembershipPauseSource.CompanyClosure && p.CancelledAt == null && p.ActualEndsOn == null))
            .OrderBy(m => m.StartsOn)
            .ToListAsync();
    }

    public async Task<bool> AnyActiveCompany(IUnitOfWork uow, Guid organizationId) =>
        await uow.Context.Companies.AnyAsync(c => c.OrganizationId == organizationId && c.IsActive);

    public async Task<ClientMembership> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.client_memberships WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE");
        return await WithGraph(uow.Context.ClientMemberships)
            .SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.Id == id);
    }

    public async Task LockClient(IUnitOfWork uow, Guid organizationId, Guid clientId)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.clients WHERE organization_id = {organizationId} AND id = {clientId} FOR UPDATE");
    }

    public Task<List<ClientMembership>> GetCoverageCandidates(IUnitOfWork uow, Guid organizationId, Guid clientId)
    {
        return WithGraph(uow.Context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.ClientId == clientId && m.VoidedAt == null)
            .ToListAsync();
    }

    public Task<List<ClientMembership>> GetCapacityCandidates(IUnitOfWork uow, Guid organizationId, Guid planId, DateOnly today)
    {
        return WithGraph(uow.Context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.VoidedAt == null && (m.EndsOn == null || m.EndsOn >= today)
                        && (m.MembershipPlanId == planId || (m.PendingPlanVersion != null && m.PendingPlanVersion.MembershipPlanId == planId)))
            .ToListAsync();
    }

    public async Task<List<ClientMembership>> GetActiveForPlanForUpdate(IUnitOfWork uow, Guid organizationId, Guid planId, DateOnly today)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $@"SELECT 1 FROM dunelight.client_memberships WHERE organization_id = {organizationId} AND membership_plan_id = {planId}
               AND voided_at IS NULL AND (ends_on IS NULL OR ends_on >= {today}) ORDER BY id FOR UPDATE");
        return await WithGraph(uow.Context.ClientMemberships)
            .Where(m => m.OrganizationId == organizationId && m.MembershipPlanId == planId && m.VoidedAt == null
                        && (m.EndsOn == null || m.EndsOn >= today))
            .ToListAsync();
    }

    public void AddAudit(IUnitOfWork uow, ClientMembershipAuditLog entry)
    {
        uow.Context.ClientMembershipAuditLog.Add(entry);
    }

    private static IQueryable<ClientMembership> WithGraph(IQueryable<ClientMembership> memberships) => memberships
        .AsSplitQuery()
        .Include(m => m.Plan)
        .Include(m => m.Pauses)
        .Include(m => m.PlanVersion).ThenInclude(v => v.Services).ThenInclude(s => s.Service)
        .Include(m => m.PlanVersion).ThenInclude(v => v.Companies).ThenInclude(c => c.Company)
        .Include(m => m.PlanVersion).ThenInclude(v => v.UsageLimits).ThenInclude(l => l.Service)
        .Include(m => m.PlanVersion).ThenInclude(v => v.PriceBenefits).ThenInclude(b => b.Service)
        .Include(m => m.PendingPlanVersion).ThenInclude(v => v.Plan)
        .Include(m => m.PendingPlanVersion).ThenInclude(v => v.Services).ThenInclude(s => s.Service)
        .Include(m => m.PendingPlanVersion).ThenInclude(v => v.Companies).ThenInclude(c => c.Company)
        .Include(m => m.PendingPlanVersion).ThenInclude(v => v.UsageLimits).ThenInclude(l => l.Service)
        .Include(m => m.PendingPlanVersion).ThenInclude(v => v.PriceBenefits).ThenInclude(b => b.Service)
        .Include(m => m.DisplacedPlanVersion).ThenInclude(v => v.Plan)
        .Include(m => m.DisplacedPlanVersion).ThenInclude(v => v.Services)
        .Include(m => m.DisplacedPlanVersion).ThenInclude(v => v.Companies)
        .Include(m => m.PlanUpdateSkippedVersion)
        .Include(m => m.Periods)
        .Include(m => m.Charges).ThenInclude(c => c.Period)
        .Include(m => m.Charges).ThenInclude(c => c.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment);

    public async Task<List<Guid>> GetOrganizationsWithActiveMemberships()
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientMemberships
            .Where(m => m.VoidedAt == null)
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Guid>> GetRenewalCandidateIds(Guid organizationId, DateOnly today)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.ClientMemberships
            .Where(m => m.OrganizationId == organizationId && m.VoidedAt == null && (m.EndsOn == null || m.EndsOn >= today))
            .OrderBy(m => m.Id)
            .Select(m => m.Id)
            .ToListAsync();
    }

    public async Task<MembershipCharge> GetChargeForUpdate(IUnitOfWork uow, Guid organizationId, Guid chargeId)
    {
        await uow.Context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM dunelight.membership_charges WHERE organization_id = {organizationId} AND id = {chargeId} FOR UPDATE");
        return await uow.Context.MembershipCharges
            .Include(c => c.Period)
            .Include(c => c.Membership)
            .Include(c => c.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == chargeId);
    }

    public async Task RefreshChargeSettlement(IUnitOfWork uow, IReadOnlyCollection<Guid> chargeIds)
    {
        if (chargeIds.Count == 0)
            return;

        List<MembershipCharge> charges = await uow.Context.MembershipCharges
            .Include(c => c.CheckoutItems).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .Where(c => chargeIds.Contains(c.Id))
            .ToListAsync();
        foreach (MembershipCharge charge in charges)
        {
            // Projekcija (Q16.3) — svježe iz aktivnih alokacija, u istoj transakciji kao njihova promjena.
            await uow.Context.Entry(charge).Collection(c => c.CheckoutItems).Query()
                .Include(i => i.Allocations).ThenInclude(a => a.Payment).LoadAsync();
            decimal settled = Math.Min(Utils.MembershipChargeSettlement.Settled(charge), charge.Amount);
            charge.SettledAmount = settled;
            charge.SettlementStatus = Utils.MembershipChargeSettlement.SettlementStatusOf(charge.Amount, settled);
        }

        await uow.Context.SaveChangesAsync();
    }

    public async Task<List<ClientMembership>> GetActiveForPlan(Guid organizationId, Guid planId, DateOnly today)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await WithGraph(context.ClientMemberships.AsNoTracking())
            .Where(m => m.OrganizationId == organizationId && m.MembershipPlanId == planId && m.VoidedAt == null
                        && (m.EndsOn == null || m.EndsOn >= today))
            .ToListAsync();
    }
}
