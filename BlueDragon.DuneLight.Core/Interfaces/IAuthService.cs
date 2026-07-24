using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Auth;

namespace BlueDragon.DuneLight.Core.Interfaces;

public interface IAuthService
{
    /// <summary>Baca BusinessRuleException(AUTH_ORGANIZATION_SLUG_TAKEN) ako naziv organizacije već postoji.</summary>
    Task<AuthResponse> Register(RegisterRequest request);

    /// <summary>Baca UnauthorizedAppException(AUTH_INVALID_CREDENTIALS) ako podaci ne odgovaraju aktivnom korisniku.</summary>
    Task<AuthResponse> Login(LoginRequest request);

    /// <summary>Prijavljeni korisnik mijenja vlastitu lozinku. Baca UnauthorizedAppException(AUTH_CURRENT_PASSWORD_INVALID) ako trenutna lozinka nije ispravna.</summary>
    Task ChangePassword(Guid userId, ChangePasswordRequest request);
}
