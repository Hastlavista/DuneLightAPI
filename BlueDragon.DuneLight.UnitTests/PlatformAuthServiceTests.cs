using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models.Management;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services.Management;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Pure unit test (fake handler/jwt service, no DB) proving PlatformAuthService.Login only ever
/// queries platform_accounts (its sole dependency is IPlatformAccountHandler - there is no IAuthHandler/Users
/// dependency anywhere in this class), and that identity separation (§16 of the platform-identity task) holds:
/// a tenant User's credentials can never succeed here because this service has no way to even look one up.</summary>
public class PlatformAuthServiceTests
{
    private class FakePlatformAccountHandler : IPlatformAccountHandler
    {
        private readonly PlatformAccount _account;

        public FakePlatformAccountHandler(PlatformAccount account) => _account = account;

        public Task<PlatformAccount> GetByEmail(string email) =>
            Task.FromResult(_account != null && _account.Email == email ? _account : null);

        public Task<PlatformAccount> GetById(Guid id) => throw new NotImplementedException();
        public Task<bool> IsActive(Guid id) => throw new NotImplementedException();
        public Task<PlatformAccount> Create(PlatformAccount account) => throw new NotImplementedException();
    }

    private class FakePlatformJwtService : IPlatformJwtService
    {
        public (string Token, DateTime Expiration) GenerateToken(Guid platformAccountId, string email) =>
            ("fake-token", DateTime.UtcNow.AddHours(1));
    }

    private static PlatformAccount ActiveAccount(string email, string password) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        PasswordHash = PlatformPasswordHasher.Hash(password),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Correct_credentials_for_an_active_account_succeed()
    {
        PlatformAccount account = ActiveAccount("operator@platform.test", "correct-password");
        IPlatformAuthService service = new PlatformAuthService(new FakePlatformAccountHandler(account), new FakePlatformJwtService(), NullLogger<PlatformAuthService>.Instance);

        PlatformAuthResponse response = await service.Login(new PlatformLoginRequest { Email = "operator@platform.test", Password = "correct-password" });

        Assert.Equal(account.Id, response.PlatformAccountId);
        Assert.Equal("fake-token", response.Token);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        PlatformAccount account = ActiveAccount("operator@platform.test", "correct-password");
        IPlatformAuthService service = new PlatformAuthService(new FakePlatformAccountHandler(account), new FakePlatformJwtService(), NullLogger<PlatformAuthService>.Instance);

        UnauthorizedAppException ex = await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.Login(new PlatformLoginRequest { Email = "operator@platform.test", Password = "wrong-password" }));
        Assert.Equal(ErrorCodes.AuthInvalidCredentials, ex.Code);
    }

    [Fact]
    public async Task Unknown_email_is_rejected()
    {
        IPlatformAuthService service = new PlatformAuthService(new FakePlatformAccountHandler(null), new FakePlatformJwtService(), NullLogger<PlatformAuthService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.Login(new PlatformLoginRequest { Email = "nobody@platform.test", Password = "anything" }));
    }

    [Fact]
    public async Task An_inactive_account_is_rejected_even_with_the_correct_password()
    {
        PlatformAccount account = ActiveAccount("operator@platform.test", "correct-password");
        account.IsActive = false;
        IPlatformAuthService service = new PlatformAuthService(new FakePlatformAccountHandler(account), new FakePlatformJwtService(), NullLogger<PlatformAuthService>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.Login(new PlatformLoginRequest { Email = "operator@platform.test", Password = "correct-password" }));
    }
}
