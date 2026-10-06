using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;

namespace BlueDragon.DuneLight.Infrastructure.Handlers.Implementations;

public class AuthHandler : IAuthHandler
{
    private readonly DatabaseSettings _databaseSettings;

    public AuthHandler(DatabaseSettings databaseSettings)
    {
        _databaseSettings = databaseSettings;
    }

    public async Task<bool> SlugExists(string slug)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Organizations.AnyAsync(o => o.Slug == slug);
    }

    public async Task AddOrganization(Organization organization)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
    }

    public async Task AddOrganization(IUnitOfWork uow, Organization organization)
    {
        uow.Context.Organizations.Add(organization);
        await uow.Context.SaveChangesAsync();
    }

    public async Task<Organization> GetOrganizationBySlug(string slug)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Organizations.SingleOrDefaultAsync(o => o.Slug == slug);
    }

    public async Task AddUser(User user)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    public async Task AddUser(IUnitOfWork uow, User user)
    {
        uow.Context.Users.Add(user);
        await uow.Context.SaveChangesAsync();
    }

    // ADR-0020 — email korisničkog računa uspoređuje se trimano i bez obzira na velika/mala slova (lower(email)), isto
    // kao unique indeks ux_users_organization_email; vidi EmailNormalizer.

    public async Task<bool> EmailExists(Guid organizationId, string email)
    {
        string key = EmailNormalizer.ComparisonKey(email);
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Email.ToLower() == key);
    }

    public async Task<User> GetUserByCredentials(Guid organizationId, string email, string passwordHash)
    {
        string key = EmailNormalizer.ComparisonKey(email);
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.SingleOrDefaultAsync(u =>
            u.OrganizationId == organizationId && u.Email.ToLower() == key && u.PasswordHash == passwordHash);
    }

    public async Task<User> GetUserByPinCredentials(Guid organizationId, string email, string pinHash)
    {
        string key = EmailNormalizer.ComparisonKey(email);
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.SingleOrDefaultAsync(u =>
            u.OrganizationId == organizationId && u.Email.ToLower() == key && u.PinHash == pinHash);
    }

    public async Task<User> GetUserByApiKey(string apiKey)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.SingleOrDefaultAsync(u => u.ApiKey == apiKey);
    }

    public async Task<User> GetUserById(Guid userId)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.SingleOrDefaultAsync(u => u.Id == userId);
    }

    public async Task UpdatePasswordHash(Guid userId, string passwordHash)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        User existing = await context.Users.SingleOrDefaultAsync(u => u.Id == userId);
        if (existing == null)
            throw new ArgumentException($"User with id {userId} does not exist");

        existing.PasswordHash = passwordHash;
        context.Users.Update(existing);
        await context.SaveChangesAsync();
    }

    public async Task UpdatePinHash(Guid userId, string pinHash)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        User existing = await context.Users.SingleOrDefaultAsync(u => u.Id == userId);
        if (existing == null)
            throw new ArgumentException($"User with id {userId} does not exist");

        existing.PinHash = pinHash;
        context.Users.Update(existing);
        await context.SaveChangesAsync();
    }
}
