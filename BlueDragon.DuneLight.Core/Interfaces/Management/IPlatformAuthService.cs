using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.DTOs.Management;

namespace BlueDragon.DuneLight.Core.Interfaces.Management;

public interface IPlatformAuthService
{
    /// <summary>Throws UnauthorizedAppException(AUTH_INVALID_CREDENTIALS) if the credentials don't match an
    /// active PlatformAccount. Only ever queries platform_accounts — never the tenant Users table, even if the
    /// email happens to also belong to a tenant User (see PlatformAccount's class doc).</summary>
    Task<PlatformAuthResponse> Login(PlatformLoginRequest request);
}
