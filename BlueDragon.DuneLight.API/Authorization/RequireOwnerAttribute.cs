using System;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueDragon.DuneLight.API.Authorization;

/// <summary>
/// Za radnje koje namjerno zaobilaze cijeli grant sustav (upravljanje GrantGroup/Role — dodjela ovlasti
/// drugima) — dopušteno samo korisniku s User.IsOwner=true, bez obzira koje grantove njegova grupa ima.
/// Namjerno uže od [RequireGrant]: dok se ne pokaže potreba da i ne-Owner Admin upravlja dozvolama.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireOwnerAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        GrantContext grantContext = await GrantAuthorization.Resolve(context);
        if (grantContext == null)
            return;

        if (!grantContext.IsOwner)
            context.Result = new ForbidResult();
    }
}