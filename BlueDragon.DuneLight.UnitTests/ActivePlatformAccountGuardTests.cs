using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services.Management;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Pure unit test (fake handler, no DB) - same style as the old PlatformAccessGuardTests. Proves the
/// "is this PlatformBearer token still usable" decision is driven exclusively by platform_accounts.IsActive,
/// and that the guard never touches anything tenant-shaped (there is no tenant handler dependency at all in
/// its constructor).</summary>
public class ActivePlatformAccountGuardTests
{
    private class FakePlatformAccountHandler : IPlatformAccountHandler
    {
        private readonly Guid _activeAccountId;

        public FakePlatformAccountHandler(Guid activeAccountId) => _activeAccountId = activeAccountId;

        public Task<PlatformAccount> GetByEmail(string email) => throw new NotImplementedException();
        public Task<PlatformAccount> GetById(Guid id) => throw new NotImplementedException();
        public Task<bool> IsActive(Guid id) => Task.FromResult(id == _activeAccountId);
        public Task<PlatformAccount> Create(PlatformAccount account) => throw new NotImplementedException();
    }

    [Fact]
    public async Task An_active_account_id_is_reported_active()
    {
        Guid accountId = Guid.NewGuid();
        IActivePlatformAccountGuard guard = new ActivePlatformAccountGuard(new FakePlatformAccountHandler(accountId));

        Assert.True(await guard.IsActive(accountId));
    }

    [Fact]
    public async Task An_unknown_or_deactivated_account_id_is_reported_inactive()
    {
        Guid activeAccountId = Guid.NewGuid();
        Guid otherAccountId = Guid.NewGuid();
        IActivePlatformAccountGuard guard = new ActivePlatformAccountGuard(new FakePlatformAccountHandler(activeAccountId));

        Assert.False(await guard.IsActive(otherAccountId));
    }
}
