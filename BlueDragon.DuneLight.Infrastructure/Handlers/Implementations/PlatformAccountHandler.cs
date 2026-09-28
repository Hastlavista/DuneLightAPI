using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class PlatformAccountHandler : IPlatformAccountHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public PlatformAccountHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<PlatformAccount> GetByEmail(string email)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PlatformAccounts.SingleOrDefaultAsync(a => a.Email == email);
    }

    public async Task<PlatformAccount> GetById(Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PlatformAccounts.SingleOrDefaultAsync(a => a.Id == id);
    }

    public async Task<bool> IsActive(Guid id)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.PlatformAccounts.AnyAsync(a => a.Id == id && a.IsActive);
    }

    public async Task<PlatformAccount> Create(PlatformAccount account)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.PlatformAccounts.Add(account);
        await context.SaveChangesAsync();
        return account;
    }
}
