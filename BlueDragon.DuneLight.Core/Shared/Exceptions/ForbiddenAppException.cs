using System;
using System.Collections.Generic;
using System.Linq;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Autentificiran pozivatelj nema pravo na radnju. API: 403 FORBIDDEN s <see cref="ForbiddenDetails"/> (T1-5): vrsta
/// odbijanja i svi grantovi koji nedostaju. Stvara se samo kroz tvorničke metode, da svaki 403 nosi detalje.</summary>
public class ForbiddenAppException : Exception
{
    public string Code { get; }

    public ForbiddenDetails Details { get; }

    private ForbiddenAppException(string message, ForbiddenDetails details, string? code = null) : base(message)
    {
        Code = code ?? ErrorCodes.Forbidden;
        Details = details;
    }

    /// <summary>Nedostaju grantovi; potreban je SVAKI naveden (npr. ponovno otvaranje s Completed i NoShow sudjelovanjem).</summary>
    public static ForbiddenAppException MissingGrants(string message, IEnumerable<string>? grants) =>
        new(message, new ForbiddenDetails { Reason = ForbiddenReason.MissingGrant, RequiredGrants = Distinct(grants), Match = GrantMatch.All });

    public static ForbiddenAppException MissingGrant(string message, string grant) => MissingGrants(message, new[] { grant });

    /// <summary>Nedostaje bilo koji od navedenih grantova (dovoljan je jedan).</summary>
    public static ForbiddenAppException MissingAnyGrant(string message, params string[] grants) =>
        new(message, new ForbiddenDetails { Reason = ForbiddenReason.MissingGrant, RequiredGrants = Distinct(grants), Match = GrantMatch.Any });

    /// <summary>Own opseg na tuđem resursu; potreban je opseg all (navedeni grant(ovi), bilo koji). Kod ostaje NOT_OWNER
    /// (do T1 je to bio 409); status je 403 s razlogom OutOfScope.</summary>
    public static ForbiddenAppException OutOfScope(string message, params string[] allScopeGrants) =>
        new(message, new ForbiddenDetails
        {
            Reason = ForbiddenReason.OutOfScope,
            RequiredGrants = Distinct(allScopeGrants),
            Match = GrantMatch.Any,
            CurrentScope = AccessScope.Own,
            RequiredScope = AccessScope.All
        }, ErrorCodes.NotOwner);

    private static List<string> Distinct(IEnumerable<string>? grants) =>
        (grants ?? Enumerable.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
}
