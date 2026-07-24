using System;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// Vrijednost role-claima koju očekuju autorizacijski atributi u svim modulima ([Authorize(Roles = "Admin")]).
/// UserRole nazivi članova su namjerno identični claim-stringovima (Admin/Member/Reception) —
/// nema prevođenja, samo jedinstven izvor imena kroz cijeli ugovor (request i response).
/// </summary>
public static class UserRoleClaims
{
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string Reception = "Reception";

    public static string ToClaimValue(UserRole role)
    {
        return role switch
        {
            UserRole.Admin => Admin,
            UserRole.Member => Member,
            UserRole.Reception => Reception,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
        };
    }
}
