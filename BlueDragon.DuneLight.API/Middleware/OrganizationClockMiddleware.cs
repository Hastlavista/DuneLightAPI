using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Infrastructure.Time;
using Microsoft.AspNetCore.Http;

namespace BlueDragon.DuneLight.API.Middleware;

/// <summary>
/// T1 — postavlja organizaciju poslovnog sata za cijeli tenant zahtjev (claim "organizationId" iz tokena), pa svaki poziv
/// TimeProvidera u servisima vidi sat te organizacije (uključujući simulirani pomak testnih alata). Zahtjev bez organizacije
/// (registracija, Management, platforma) koristi stvarni sat.
/// </summary>
public class OrganizationClockMiddleware
{
    private readonly RequestDelegate _next;

    public OrganizationClockMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string value = context.User?.FindFirst("organizationId")?.Value;
        Guid? organizationId = Guid.TryParse(value, out Guid parsed) ? parsed : null;

        using IDisposable clock = OrganizationClockContext.Use(organizationId);
        await _next(context);
    }
}
