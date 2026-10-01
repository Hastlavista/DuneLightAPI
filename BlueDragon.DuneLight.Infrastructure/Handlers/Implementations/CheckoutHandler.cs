using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Appointments;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Checkouts;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class CheckoutHandler : ICheckoutHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public CheckoutHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    private static IQueryable<Checkout> IncludeGraph(IQueryable<Checkout> query)
    {
        return query
            .Include(c => c.Company)
            .Include(c => c.Client)
            // Phase D3B3B: stavka usluge -> sudjelovanje (s potrošnjom paketa, AutoInclude) i SVE njegove stavke kroz
            // vrijeme s alokacijama — namirenje se računa na granici sudjelovanja (ParticipationSettlement).
            .Include(c => c.Items).ThenInclude(i => i.Participation).ThenInclude(p => p.CheckoutItems)
                .ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .Include(c => c.Items).ThenInclude(i => i.Participation).ThenInclude(p => p.Booking)
            .Include(c => c.Items).ThenInclude(i => i.Allocations).ThenInclude(a => a.Payment)
            .Include(c => c.Payments);
    }

    public async Task Add(IUnitOfWork uow, Checkout checkout)
    {
        uow.Context.Checkouts.Add(checkout);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<Checkout> GetForUpdate(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return await uow.Context.Checkouts
            .FromSqlInterpolated($@"SELECT * FROM dunelight.checkouts
                WHERE organization_id = {organizationId} AND id = {id}
                FOR UPDATE")
            .SingleOrDefaultAsync();
    }

    public async Task<Checkout> GetGraph(IUnitOfWork uow, Guid organizationId, Guid id)
    {
        return await IncludeGraph(uow.Context.Checkouts.AsQueryable())
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
    }

    public async Task<Checkout> GetGraph(Guid organizationId, Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Checkouts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(c => c.OrganizationId == organizationId && c.Id == id);
    }

    public async Task<List<Checkout>> GetByClient(Guid organizationId, Guid clientId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Checkouts)
            .AsSplitQuery()
            .Where(c => c.OrganizationId == organizationId && c.ClientId == clientId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task Update(IUnitOfWork uow, Checkout checkout)
    {
        uow.Context.Checkouts.Update(checkout);
        await uow.Context.SaveChangesAsync();
    }

    public async Task AddItem(IUnitOfWork uow, CheckoutItem item)
    {
        uow.Context.CheckoutItems.Add(item);
        await uow.Context.SaveChangesAsync();
    }

    public async Task RemoveItem(IUnitOfWork uow, CheckoutItem item)
    {
        uow.Context.CheckoutItems.Remove(item);
        await uow.Context.SaveChangesAsync();
    }

    public Task<CheckoutItem> GetItem(IUnitOfWork uow, Guid organizationId, Guid checkoutId, Guid itemId)
    {
        return uow.Context.CheckoutItems
            .Include(i => i.Participation)
            .Include(i => i.Allocations).ThenInclude(a => a.Payment)
            .SingleOrDefaultAsync(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.Id == itemId);
    }

    public async Task AddPayment(IUnitOfWork uow, Payment payment)
    {
        uow.Context.Payments.Add(payment);
        await uow.Context.SaveChangesAsync();
    }

    public async Task UpdatePayment(IUnitOfWork uow, Payment payment)
    {
        uow.Context.Payments.Update(payment);
        await uow.Context.SaveChangesAsync();
    }

    public Task<Payment> GetPayment(IUnitOfWork uow, Guid organizationId, Guid checkoutId, Guid paymentId)
    {
        return uow.Context.Payments
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.CheckoutId == checkoutId && p.Id == paymentId);
    }

    public async Task<List<CheckoutItem>> GetItemsForParticipation(Guid organizationId, Guid participationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.CheckoutItems
            .Include(i => i.Allocations).ThenInclude(a => a.Payment)
            .Where(i => i.OrganizationId == organizationId && i.BookingSegmentParticipationId == participationId)
            .ToListAsync();
    }

    public Task<List<CheckoutItem>> GetItemsForParticipation(IUnitOfWork uow, Guid organizationId, Guid participationId)
    {
        return uow.Context.CheckoutItems
            .Include(i => i.Allocations).ThenInclude(a => a.Payment)
            .Where(i => i.OrganizationId == organizationId && i.BookingSegmentParticipationId == participationId)
            .ToListAsync();
    }

    public async Task LockParticipations(IUnitOfWork uow, Guid organizationId, IEnumerable<Guid> participationIds)
    {
        // Stabilan redoslijed (po id) — dvije transakcije koje zaključavaju isti skup ne mogu se zaključati u krug.
        foreach (Guid id in participationIds.Distinct().OrderBy(x => x))
            await uow.Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM dunelight.booking_segment_participations WHERE organization_id = {organizationId} AND id = {id} FOR UPDATE");
    }

    public Task<List<Guid>> GetServiceParticipationIds(IUnitOfWork uow, Guid organizationId, Guid checkoutId)
    {
        return uow.Context.CheckoutItems
            .Where(i => i.OrganizationId == organizationId && i.CheckoutId == checkoutId && i.BookingSegmentParticipationId != null)
            .Select(i => i.BookingSegmentParticipationId.Value)
            .Distinct()
            .ToListAsync();
    }

    public async Task<List<Checkout>> GetOpenByCompany(Guid organizationId, Guid companyId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await IncludeGraph(context.Checkouts)
            .AsSplitQuery()
            .Where(c => c.OrganizationId == organizationId && c.CompanyId == companyId && c.Status == CheckoutStatus.Open)
            .ToListAsync();
    }

    public async Task<decimal> GetCompletedPaymentAmountForCompanyOnDate(
        Guid organizationId, Guid companyId, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Payments
            .Where(p => p.OrganizationId == organizationId && p.Status == PaymentStatus.Completed &&
                p.CreatedAt >= dayStart && p.CreatedAt < dayEnd && p.Checkout.CompanyId == companyId)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
    }
}
