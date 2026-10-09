using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BlueDragon.DuneLight.Core.Interfaces.Management;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using Microsoft.IdentityModel.Tokens;

namespace BlueDragon.DuneLight.Infrastructure.Services.Management;

public class PlatformJwtService : IPlatformJwtService
{
    private readonly PlatformJwtSettings _platformJwtSettings;

    public PlatformJwtService(PlatformJwtSettings platformJwtSettings)
    {
        _platformJwtSettings = platformJwtSettings;
    }

    public (string Token, DateTimeOffset Expiration) GenerateToken(Guid platformAccountId, string email)
    {
        SymmetricSecurityKey securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_platformJwtSettings.SecretKey));
        SigningCredentials credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        Claim[] claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, platformAccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        ];

        // T1-7: istek tokena je instant (DateTimeOffset, UTC) po SISTEMSKOM satu — simulirani poslovni sat ne utječe na tokene.
        DateTimeOffset expiration = TimeProvider.System.GetUtcNow().AddHours(_platformJwtSettings.ExpirationHours);
        JwtSecurityToken token = new JwtSecurityToken(
            issuer: _platformJwtSettings.Issuer,
            audience: _platformJwtSettings.Audience,
            claims: claims,
            expires: expiration.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
    }
}
