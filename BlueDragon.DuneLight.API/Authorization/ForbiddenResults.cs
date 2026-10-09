using System;
using BlueDragon.DuneLight.Core.Enums;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>T1 — 403 iz autorizacijskih filtera u istom obliku kao ForbiddenAppException u middlewareu: { error: { code, message,
/// details: ForbiddenDetails } }, sa svim grantovima koji nedostaju (FE-ADR-0003).</summary>
internal static class ForbiddenResults
{
    /// <summary>[RequireGrant] s popisom grantova: dovoljan je bilo koji, korisnik nema nijedan.</summary>
    public static IActionResult MissingAnyGrant(string[] grants) =>
        Create("Nemate ovlasti za ovu radnju.", new ForbiddenDetails
        {
            Reason = ForbiddenReason.MissingGrant,
            RequiredGrants = new(grants),
            Match = grants.Length > 1 ? GrantMatch.Any : GrantMatch.All
        });

    /// <summary>Korisnik bez granta nije dodijeljen poslovnici iz rute.</summary>
    public static IActionResult CompanyNotAssigned(string[] grants, Guid? companyId) =>
        Create("Nemate ovlasti za ovu poslovnicu.", new ForbiddenDetails
        {
            Reason = ForbiddenReason.CompanyNotAssigned,
            RequiredGrants = new(grants),
            Match = grants.Length > 1 ? GrantMatch.Any : GrantMatch.All,
            CompanyId = companyId
        });

    private static IActionResult Create(string message, ForbiddenDetails details) =>
        new ObjectResult(new ErrorResponse(new ErrorDetail(ErrorCodes.Forbidden, message, details)))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
}
