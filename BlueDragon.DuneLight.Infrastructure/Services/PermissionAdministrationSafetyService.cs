using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Handlers.Interfaces;
using BlueDragon.DuneLight.Infrastructure.UnitOfWork;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>Vidi IPermissionAdministrationSafetyService.</summary>
public class PermissionAdministrationSafetyService : IPermissionAdministrationSafetyService
{
    private readonly IGrantGroupHandler _grantGroupHandler;

    public PermissionAdministrationSafetyService(IGrantGroupHandler grantGroupHandler)
    {
        _grantGroupHandler = grantGroupHandler;
    }

    public async Task EnsureRetainsPermissionAdmin(
        Guid organizationId,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null)
    {
        bool retained = await _grantGroupHandler.HasActiveUserWithGrant(
            organizationId,
            Grants.PermissionsManage,
            overrideGrantGroupId,
            overrideGrantGroupGrants,
            overrideUserId,
            overrideUserGrantGroupIds);

        if (!retained)
            throw new BusinessRuleException(
                ErrorCodes.LastPermissionAdminRequired,
                "Organizacija mora imati barem jednog aktivnog korisnika s ovlašću upravljanja dozvolama (permissions.manage) — ova promjena bi to onemogućila.");
    }

    public async Task EnsureRetainsPermissionAdminInTransaction(IUnitOfWork uow, Guid organizationId)
    {
        bool retained = await _grantGroupHandler.HasActiveUserWithGrantInTransaction(uow, organizationId, Grants.PermissionsManage);

        if (!retained)
            throw new BusinessRuleException(
                ErrorCodes.LastPermissionAdminRequired,
                "Organizacija mora imati barem jednog aktivnog korisnika s ovlašću upravljanja dozvolama (permissions.manage) — ova promjena bi to onemogućila.");
    }
}
