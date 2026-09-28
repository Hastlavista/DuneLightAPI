using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services.Management;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>DB-backed, same isolated-row + finally-cleanup pattern as PlatformAccountHandlerTests. Proves the
/// idempotent "create once, never reset password or reactivate" semantics the task explicitly requires - a
/// restart must never silently undo a deliberate password change or deactivation.</summary>
public class PlatformAccountBootstrapperTests
{
    private const string LocalConnectionString = "Host=localhost;Database=postgres;Password=root1234;Username=postgres";

    private static DatabaseSettings Settings() => new() { ConnectionString = LocalConnectionString };

    [Fact]
    public async Task Creates_the_account_when_none_exists_for_the_bootstrap_email()
    {
        string email = $"bootstrap-{Guid.NewGuid():N}@platform.test";
        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        try
        {
            PlatformAccountBootstrapper bootstrapper = new(handler, new PlatformSettings { BootstrapEmail = email, BootstrapPassword = "initial-password" }, NullLogger<PlatformAccountBootstrapper>.Instance);

            await bootstrapper.StartAsync(CancellationToken.None);

            PlatformAccount created = await handler.GetByEmail(email);
            Assert.NotNull(created);
            Assert.True(created.IsActive);
            Assert.True(PlatformPasswordHasher.Verify("initial-password", created.PasswordHash));
        }
        finally
        {
            await Cleanup(email);
        }
    }

    [Fact]
    public async Task A_second_run_does_not_reset_the_password_of_an_already_bootstrapped_account()
    {
        string email = $"bootstrap-{Guid.NewGuid():N}@platform.test";
        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        try
        {
            PlatformAccountBootstrapper bootstrapper = new(handler, new PlatformSettings { BootstrapEmail = email, BootstrapPassword = "initial-password" }, NullLogger<PlatformAccountBootstrapper>.Instance);
            await bootstrapper.StartAsync(CancellationToken.None);

            // Simulate the operator having since changed their password to something the bootstrap config no
            // longer matches.
            await using (DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString))
            {
                PlatformAccount account = context.PlatformAccounts.Single(a => a.Email == email);
                account.PasswordHash = PlatformPasswordHasher.Hash("operator-changed-this");
                await context.SaveChangesAsync();
            }

            // A restart with the SAME (now stale) bootstrap config must not silently overwrite it.
            await bootstrapper.StartAsync(CancellationToken.None);

            PlatformAccount afterSecondRun = await handler.GetByEmail(email);
            Assert.True(PlatformPasswordHasher.Verify("operator-changed-this", afterSecondRun.PasswordHash));
            Assert.False(PlatformPasswordHasher.Verify("initial-password", afterSecondRun.PasswordHash));
        }
        finally
        {
            await Cleanup(email);
        }
    }

    [Fact]
    public async Task A_second_run_does_not_reactivate_a_deliberately_deactivated_account()
    {
        string email = $"bootstrap-{Guid.NewGuid():N}@platform.test";
        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        try
        {
            PlatformAccountBootstrapper bootstrapper = new(handler, new PlatformSettings { BootstrapEmail = email, BootstrapPassword = "initial-password" }, NullLogger<PlatformAccountBootstrapper>.Instance);
            await bootstrapper.StartAsync(CancellationToken.None);

            await using (DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString))
            {
                PlatformAccount account = context.PlatformAccounts.Single(a => a.Email == email);
                account.IsActive = false;
                await context.SaveChangesAsync();
            }

            await bootstrapper.StartAsync(CancellationToken.None);

            PlatformAccount afterSecondRun = await handler.GetByEmail(email);
            Assert.False(afterSecondRun.IsActive);
        }
        finally
        {
            await Cleanup(email);
        }
    }

    [Fact]
    public async Task Blank_bootstrap_email_or_password_is_a_no_op()
    {
        IPlatformAccountHandler handler = new PlatformAccountHandler(Settings());
        PlatformAccountBootstrapper bootstrapper = new(handler, new PlatformSettings { BootstrapEmail = "", BootstrapPassword = "" }, NullLogger<PlatformAccountBootstrapper>.Instance);

        await bootstrapper.StartAsync(CancellationToken.None);
        // No exception, and nothing created - asserting the absence of a side effect is the point here.
    }

    private static async Task Cleanup(string email)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(LocalConnectionString);
        context.PlatformAccounts.RemoveRange(context.PlatformAccounts.Where(a => a.Email == email));
        await context.SaveChangesAsync();
    }
}
