using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Enums;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// T1 — <c>error.details</c> svakog 403 odgovora (backend T1-5, frontend FE-ADR-0003): vrsta odbijanja i SVI grantovi koji
/// nedostaju, da korisnik odjednom vidi sve što mu treba.
/// </summary>
public class ForbiddenDetails
{
    public ForbiddenReason Reason { get; set; }

    /// <summary>Grantovi koji nedostaju (MissingGrant) ili grant potrebnog opsega (OutOfScope, CompanyNotAssigned).</summary>
    public List<string> RequiredGrants { get; set; } = new();

    public GrantMatch Match { get; set; } = GrantMatch.All;

    /// <summary>Samo OutOfScope: opseg koji korisnik ima.</summary>
    public AccessScope? CurrentScope { get; set; }

    /// <summary>Samo OutOfScope: potreban opseg.</summary>
    public AccessScope? RequiredScope { get; set; }

    /// <summary>Samo CompanyNotAssigned.</summary>
    public Guid? CompanyId { get; set; }
}

/// <summary>T1 — tipiziran oblik 403 odgovora za Swagger (isti JSON kao <see cref="ErrorResponse"/>).</summary>
public class ForbiddenErrorResponse
{
    public ForbiddenErrorDetail Error { get; set; }
}

public class ForbiddenErrorDetail
{
    public string Code { get; set; }
    public string Message { get; set; }
    public ForbiddenDetails Details { get; set; }
}
