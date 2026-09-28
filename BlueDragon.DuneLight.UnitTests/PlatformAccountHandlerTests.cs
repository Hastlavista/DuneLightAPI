using System;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed verification against a real platform_accounts table - same isolated-row + finally-cleanup
/// pattern as the old PlatformOperatorHandlerTests, but with NO organization/user setup at all, since
/// PlatformAccount has no FK to anything (that's the entire point of the identity correction - see
/// PlatformAccount's class doc).</summary>
public class PlatformAccountHandlerTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static DatabaseSettings Settings() => new() { ConnectionString = LocalConnectionString };

    [Fact]
    public async Task GetByEmail_finds_an_existing_account_and_returns_null_for_an_unknown_email()
    {
        Guid accountId = Guid.NewGuid();
        string email = $"handler-test-{accountId:N}@platform.test";

        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        try
        {
            await handler.Create(new PlatformAccount { Id = accountId, Email = email, PasswordHash = "hash", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });

            PlatformAccount found = await handler.GetByEmail(email);
            Assert.NotNull(found);
            Assert.Equal(accountId, found.Id);

            Assert.Null(await handler.GetByEmail($"nonexistent-{accountId:N}@platform.test"));
        }
        finally
        {
            await Cleanup(accountId);
        }
    }

    [Fact]
    public async Task IsActive_reflects_the_row_state_and_is_false_for_an_unknown_id()
    {
        Guid accountId = Guid.NewGuid();
        string email = $"handler-test-{accountId:N}@platform.test";

        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        try
        {
            await handler.Create(new PlatformAccount { Id = accountId, Email = email, PasswordHash = "hash", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            Assert.True(await handler.IsActive(accountId));

            await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
            PlatformAccount account = context.PlatformAccounts.Single(a => a.Id == accountId);
            account.IsActive = false;
            await context.SaveChangesAsync();

            Assert.False(await handler.IsActive(accountId));
            Assert.False(await handler.IsActive(Guid.NewGuid()));
        }
        finally
        {
            await Cleanup(accountId);
        }
    }

    private static async Task Cleanup(Guid accountId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        context.PlatformAccounts.RemoveRange(context.PlatformAccounts.Where(a => a.Id == accountId));
        await context.SaveChangesAsync();
    }
}
