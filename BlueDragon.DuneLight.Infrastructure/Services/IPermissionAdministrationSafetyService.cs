using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlueDragon.DuneLight.Infrastructure.Services;

/// <summary>
/// Grant-only Tenant Authorization Refactor, Part F/G — centralna last-permission-admin lockout provjera.
/// Zamjenjuje stare "last Owner"/"last Admin (UserRole)"/"last Admin GrantGroup po imenu" ideje jednom stabilnom
/// invarijantom: organizacija mora uvijek imati barem jednog AKTIVNOG korisnika čiji EFEKTIVNI raw grantovi
/// uključuju Grants.PermissionsManage — bez obzira na ime GrantGroup-e ili legacy UserRole. Namjerno
/// centralizirano ovdje umjesto raspršenih ad-hoc provjera po servisima (vidi Part G).
/// </summary>
public interface IPermissionAdministrationSafetyService
{
    /// <summary>Baca BusinessRuleException(ErrorCodes.LastPermissionAdminRequired) ako bi, NAKON zamišljene
    /// promjene opisane override parametrima, organizacija ostala bez ijednog aktivnog korisnika s efektivnim
    /// Grants.PermissionsManage. Poziva se PRIJE same mutacije (GrantGroup Delete/raw Update/SetUserGrantGroups,
    /// Employee deaktivacija). Vidi IGrantGroupHandler.HasActiveUserWithGrant za točnu semantiku override
    /// parametara.</summary>
    Task EnsureRetainsPermissionAdmin(
        Guid organizationId,
        Guid? overrideGrantGroupId = null,
        HashSet<string> overrideGrantGroupGrants = null,
        Guid? overrideUserId = null,
        List<Guid> overrideUserGrantGroupIds = null);
}
