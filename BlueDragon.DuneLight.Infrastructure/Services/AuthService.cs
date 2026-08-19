using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Interfaces;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Models;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;
using BlueDragon.DuneLight.Infrastructure.Utils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly IAuthHandler _authHandler;
    private readonly IGrantGroupHandler _grantGroupHandler;
    private readonly IJwtService _jwtService;
    private readonly JwtSettings _jwtSettings;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public AuthService(IAuthHandler authHandler, IGrantGroupHandler grantGroupHandler, IJwtService jwtService, JwtSettings jwtSettings, IUnitOfWorkFactory unitOfWorkFactory)
    {
        _authHandler = authHandler;
        _grantGroupHandler = grantGroupHandler;
        _jwtService = jwtService;
        _jwtSettings = jwtSettings;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    public async Task<AuthResponse> Register(RegisterRequest request)
    {
        string slug = SlugGenerator.Generate(request.OrganizationName);

        bool slugExists = await _authHandler.SlugExists(slug);
        if (slugExists)
            throw new BusinessRuleException(ErrorCodes.AuthOrganizationSlugTaken, "Organizacija s ovim nazivom već postoji.");

        Organization organization = new Organization();
        organization.Id = Guid.NewGuid();
        organization.Name = request.OrganizationName;
        organization.Slug = slug;
        organization.CreatedAt = DateTimeOffset.UtcNow;

        User user = new User();
        user.Id = Guid.NewGuid();
        user.OrganizationId = organization.Id.GetValueOrDefault();
        user.Email = request.Email;
        user.PasswordHash = PasswordHasher.Hash(request.Password);
        user.ApiKey = Guid.NewGuid().ToString("N");
        user.Role = UserRole.Admin;
        // Onaj tko odradi Register je trajno Owner ove organizacije — jedini koji zaobilazi grant sustav
        // (vidi RequireOwnerAttribute). Ne dodjeljuje mu se GrantGroup jer mu ionako ništa ne treba provjeravati.
        user.IsOwner = true;
        user.IsActive = true;
        user.CreatedAt = DateTimeOffset.UtcNow;

        try
        {
            await using IUnitOfWork uow = await _unitOfWorkFactory.Begin();

            await _authHandler.AddOrganization(uow, organization);
            await _authHandler.AddUser(uow, user);
            await _grantGroupHandler.SeedDefaultGroups(uow, organization.Id.GetValueOrDefault());

            await uow.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueSlugViolation(ex))
        {
            // SlugExists gore je samo brza provjera (TOCTOU) — jedinstveni indeks na organizations.slug je
            // stvarni izvor istine kod dvije istovremene registracije s istim nazivom organizacije.
            throw new BusinessRuleException(ErrorCodes.AuthOrganizationSlugTaken, "Organizacija s ovim nazivom već postoji.");
        }

        return ToAuthResponse(user, organization);
    }

    private static bool IsUniqueSlugViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    public async Task<AuthResponse> Login(LoginRequest request)
    {
        Organization organization = await _authHandler.GetOrganizationBySlug(request.OrganizationSlug);
        if (organization == null)
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        string passwordHash = PasswordHasher.Hash(request.Password);
        User user = await _authHandler.GetUserByCredentials(organization.Id.GetValueOrDefault(), request.Email, passwordHash);

        if (user == null || !user.IsActive)
            throw new UnauthorizedAppException(ErrorCodes.AuthInvalidCredentials, "Neispravan email ili lozinka.");

        return ToAuthResponse(user, organization);
    }

    public async Task ChangePassword(Guid userId, ChangePasswordRequest request)
    {
        User user = await _authHandler.GetUserById(userId);
        if (user == null || !user.IsActive)
            throw new UnauthorizedAppException(ErrorCodes.AuthCurrentPasswordInvalid, "Trenutna lozinka nije ispravna.");

        if (user.PasswordHash != PasswordHasher.Hash(request.CurrentPassword))
            throw new UnauthorizedAppException(ErrorCodes.AuthCurrentPasswordInvalid, "Trenutna lozinka nije ispravna.");

        await _authHandler.UpdatePasswordHash(user.Id.GetValueOrDefault(), PasswordHasher.Hash(request.NewPassword));
    }

    private AuthResponse ToAuthResponse(User user, Organization organization)
    {
        string token = _jwtService.GenerateToken(
            user.Id.GetValueOrDefault(),
            user.Email,
            organization.Id.GetValueOrDefault(),
            UserRoleClaims.ToClaimValue(user.Role));

        AuthResponse response = new AuthResponse();
        response.UserId = user.Id;
        response.Email = user.Email;
        response.ApiKey = user.ApiKey;
        response.Role = UserRoleClaims.ToClaimValue(user.Role);
        response.OrganizationId = organization.Id;
        response.OrganizationName = organization.Name;
        response.OrganizationSlug = organization.Slug;
        response.Token = token;
        response.TokenExpiration = DateTime.UtcNow.AddHours(_jwtSettings.ExpirationHours);
        return response;
    }
}
