using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>
/// Kao [RequireGrant], ali dodatno propušta i zaposlenika bez navedenih grantova ako je (preko
/// employee_companies) dodijeljen poslovnici iz rutne vrijednosti "companyId" — npr. čitanje neradnih
/// dana vlastite poslovnice bez roster.templates.view/.manage granta. Owner uvijek prolazi (kao RequireGrant).
/// Ruta MORA imati "{companyId:guid}" segment.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireGrantOrAssignedCompanyAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _grants;

    public RequireGrantOrAssignedCompanyAttribute(params string[] grants)
    {
        if (grants == null || grants.Length == 0)
            throw new ArgumentException("RequireGrantOrAssignedCompany zahtijeva barem jedan grant-ključ.", nameof(grants));

        _grants = grants;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        GrantContext grantContext = await GrantAuthorization.Resolve(context);
        if (grantContext == null)
            return;

        if (grantContext.HasAny(_grants))
            return;

        if (!context.RouteData.Values.TryGetValue("companyId", out object companyIdValue) ||
            !Guid.TryParse(companyIdValue?.ToString(), out Guid companyId))
        {
            context.Result = new ForbidResult();
            return;
        }

        ClaimsPrincipal user = context.HttpContext.User;
        string userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        string organizationIdValue = user.FindFirstValue("organizationId");
        Guid userId = Guid.Parse(userIdValue!);
        Guid organizationId = Guid.Parse(organizationIdValue!);

        IEmployeeHandler employeeHandler = context.HttpContext.RequestServices.GetRequiredService<IEmployeeHandler>();
        bool isAssigned = await employeeHandler.IsUserAssignedToCompany(organizationId, userId, companyId);
        if (!isAssigned)
            context.Result = new ForbidResult();
    }
}
