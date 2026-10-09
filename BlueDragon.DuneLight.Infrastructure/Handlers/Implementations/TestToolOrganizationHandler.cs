using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.TestTools;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

/// <summary>T1 — PRIVREMENI testni alati (tablica test_tool_organizations). Uklanja se prije go-livea.</summary>
public class TestToolOrganizationHandler : ITestToolOrganizationHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public TestToolOrganizationHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<bool> OrganizationExists(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Organizations.AnyAsync(o => o.Id == organizationId);
    }

    public async Task<TestToolOrganization> Get(Guid organizationId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.TestToolOrganizations.AsNoTracking().SingleOrDefaultAsync(t => t.OrganizationId == organizationId);
    }

    public async Task SetClockOffset(Guid organizationId, long offsetSeconds, Guid? advancedBy, DateTimeOffset advancedAt)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        TestToolOrganization row = await GetOrAdd(context, organizationId, advancedAt);
        row.ClockOffsetSeconds = offsetSeconds;
        row.ClockAdvancedAt = advancedAt;
        row.ClockAdvancedBy = advancedBy;
        await context.SaveChangesAsync();
    }

    public async Task MarkDemo(Guid organizationId, string demoLevel, DateTimeOffset createdAt)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        TestToolOrganization row = await GetOrAdd(context, organizationId, createdAt);
        row.IsDemo = true;
        row.DemoLevel = demoLevel;
        await context.SaveChangesAsync();
    }

    public async Task Retire(Guid organizationId, DateTimeOffset retiredAt)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        TestToolOrganization row = await GetOrAdd(context, organizationId, retiredAt);
        row.RetiredAt = retiredAt;
        await context.Users
            .Where(u => u.OrganizationId == organizationId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        await context.SaveChangesAsync();
    }

    private static async Task<TestToolOrganization> GetOrAdd(DatabaseContext context, Guid organizationId, DateTimeOffset now)
    {
        TestToolOrganization row = await context.TestToolOrganizations.SingleOrDefaultAsync(t => t.OrganizationId == organizationId);
        if (row != null)
            return row;
        row = new TestToolOrganization { OrganizationId = organizationId, CreatedAt = now };
        context.TestToolOrganizations.Add(row);
        return row;
    }
}
