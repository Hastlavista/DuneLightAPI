using System;

namespace BlueDragon.DuneLight.Core.Interfaces.Management;

/// <summary>Issues "PlatformBearer"-scheme tokens — deliberately a leaner signature than IJwtService
/// (no organizationId/role: PlatformAccount has neither).</summary>
public interface IPlatformJwtService
{
    (string Token, DateTimeOffset Expiration) GenerateToken(Guid platformAccountId, string email);
}
