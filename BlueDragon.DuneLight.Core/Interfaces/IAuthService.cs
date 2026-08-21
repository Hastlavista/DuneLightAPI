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

    /// <summary>Brzo prebacivanje korisnika na dijeljenom uređaju — izdaje nov JWT (isti oblik odgovora kao <see cref="Login"/>),
    /// bez lokalnog čuvanja starih tokena. Baca UnauthorizedAppException(AUTH_INVALID_PIN) ako podaci ne odgovaraju
    /// aktivnom korisniku s postavljenim PIN-om.</summary>
    Task<AuthResponse> PinLogin(PinLoginRequest request);

    /// <summary>Prijavljeni korisnik mijenja vlastitu lozinku. Baca UnauthorizedAppException(AUTH_CURRENT_PASSWORD_INVALID) ako trenutna lozinka nije ispravna.</summary>
    Task ChangePassword(Guid userId, ChangePasswordRequest request);

    /// <summary>Prijavljeni korisnik postavlja/mijenja vlastiti PIN — uvijek potvrđeno LOZINKOM (ne starim PIN-om),
    /// isto za prvo postavljanje i za promjenu. Baca UnauthorizedAppException(AUTH_CURRENT_PASSWORD_INVALID) ako
    /// trenutna lozinka nije ispravna.</summary>
    Task ChangePin(Guid userId, ChangePinRequest request);
}
