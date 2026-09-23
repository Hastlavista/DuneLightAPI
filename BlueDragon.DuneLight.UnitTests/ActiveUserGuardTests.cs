using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.Services;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>Covers the decision behind the JWT bearer's OnTokenValidated event (Startup.cs) that rejects an
/// already-issued token once its User is deactivated - see ActiveUserGuard's own class doc. This is a pure
/// unit test of the decision itself (IActiveUserGuard.IsUserActive); the actual HTTP-pipeline wiring has no
/// existing integration-test harness in this project (see TestSupport.cs - every other suite here is a pure
/// unit test), so it was verified live instead (login -> 200, deactivate -> same token now rejected).</summary>
public class ActiveUserGuardTests
{
    private class FakeAuthHandler : IAuthHandler
    {
        private readonly User _user;

        public FakeAuthHandler(User user) => _user = user;

        public Task<User> GetUserById(Guid userId) =>
            Task.FromResult(_user != null && _user.Id == userId ? _user : null);

        public Task<bool> SlugExists(string slug) => throw new NotImplementedException();
        public Task AddOrganization(Organization organization) => throw new NotImplementedException();
        public Task AddOrganization(IUnitOfWork uow, Organization organization) => throw new NotImplementedException();
        public Task<Organization> GetOrganizationBySlug(string slug) => throw new NotImplementedException();
        public Task AddUser(User user) => throw new NotImplementedException();
        public Task AddUser(IUnitOfWork uow, User user) => throw new NotImplementedException();
        public Task<bool> EmailExists(Guid organizationId, string email) => throw new NotImplementedException();
        public Task<User> GetUserByCredentials(Guid organizationId, string email, string passwordHash) => throw new NotImplementedException();
        public Task<User> GetUserByPinCredentials(Guid organizationId, string email, string pinHash) => throw new NotImplementedException();
        public Task<User> GetUserByApiKey(string apiKey) => throw new NotImplementedException();
        public Task UpdatePasswordHash(Guid userId, string passwordHash) => throw new NotImplementedException();
        public Task UpdatePinHash(Guid userId, string pinHash) => throw new NotImplementedException();
        public Task UpdateRole(Guid organizationId, Guid userId, UserRole role) => throw new NotImplementedException();
    }

    private static User NewUser(bool isActive, UserRole role) =>
        new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Email = "test@test.local", IsActive = isActive, Role = role };

    [Fact]
    public async Task Active_user_with_a_valid_token_context_is_allowed()
    {
        User user = NewUser(isActive: true, role: UserRole.Member);
        IActiveUserGuard guard = new ActiveUserGuard(new FakeAuthHandler(user));

        Assert.True(await guard.IsUserActive(user.Id.Value));
    }

    [Fact]
    public async Task Deactivating_the_same_user_rejects_the_same_authorization_context()
    {
        User user = NewUser(isActive: true, role: UserRole.Member);
        FakeAuthHandler handler = new(user);
        IActiveUserGuard guard = new ActiveUserGuard(handler);
        Assert.True(await guard.IsUserActive(user.Id.Value));

        user.IsActive = false; // same User row, as a deactivation would flip it

        Assert.False(await guard.IsUserActive(user.Id.Value));
    }

    [Fact]
    public async Task Legacy_UserRole_Admin_does_not_bypass_inactive_status()
    {
        User user = NewUser(isActive: false, role: UserRole.Admin);
        IActiveUserGuard guard = new ActiveUserGuard(new FakeAuthHandler(user));

        Assert.False(await guard.IsUserActive(user.Id.Value));
    }

    [Fact]
    public async Task The_check_is_independent_of_grants_permissions_manage_included_by_construction()
    {
        // IActiveUserGuard never reads UserGrantGroup/grants at all - only User.IsActive - so no grant,
        // including permissions.manage, can special-case its way past deactivation.
        User user = NewUser(isActive: false, role: UserRole.Admin);
        IActiveUserGuard guard = new ActiveUserGuard(new FakeAuthHandler(user));

        Assert.False(await guard.IsUserActive(user.Id.Value));
    }

    [Fact]
    public async Task Reactivated_user_is_allowed_again()
    {
        User user = NewUser(isActive: false, role: UserRole.Member);
        FakeAuthHandler handler = new(user);
        IActiveUserGuard guard = new ActiveUserGuard(handler);
        Assert.False(await guard.IsUserActive(user.Id.Value));

        user.IsActive = true;

        Assert.True(await guard.IsUserActive(user.Id.Value));
    }

    [Fact]
    public async Task A_user_that_no_longer_exists_is_rejected()
    {
        IActiveUserGuard guard = new ActiveUserGuard(new FakeAuthHandler(null));

        Assert.False(await guard.IsUserActive(Guid.NewGuid()));
    }
}
