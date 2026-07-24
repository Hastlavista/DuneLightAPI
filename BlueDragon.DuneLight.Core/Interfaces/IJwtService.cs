using System;

namespace BlueDragon.DuneLight.Core.Interfaces;

public interface IJwtService
{
    string GenerateToken(Guid userId, string email, Guid organizationId, string role);
}
