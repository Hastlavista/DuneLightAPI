using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Infrastructure.Domain.Contexts;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
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

    public async Task<bool> EmailExists(Guid organizationId, string email)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.AnyAsync(u => u.OrganizationId == organizationId && u.Email == email);
    }

    public async Task<User> GetUserByCredentials(Guid organizationId, string email, string passwordHash)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        return await context.Users.SingleOrDefaultAsync(u =>
            u.OrganizationId == organizationId && u.Email == email && u.PasswordHash == passwordHash);
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

    public async Task UpdateRole(Guid organizationId, Guid userId, UserRole role)
    {
        await using DatabaseContext context = DatabaseContext.GenerateContext(_databaseSettings.ConnectionString);
        User existing = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.OrganizationId == organizationId);
        if (existing == null)
            throw new ArgumentException($"User with id {userId} does not exist");

        existing.Role = role;
        context.Users.Update(existing);
        await context.SaveChangesAsync();
    }
}
