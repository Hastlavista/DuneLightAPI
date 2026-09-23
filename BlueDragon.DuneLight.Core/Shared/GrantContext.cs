using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>
/// Rezultat razrješavanja efektivnih ovlasti korisnika za jedan zahtjev. Grant-only Tenant Authorization
/// Refactor — nema više Owner bypass-a; jedini izvor istine su efektivni raw grantovi (UserGrantGroup ->
/// GrantGroup -> GrantGroupGrant). Organizacijski osnivač dobiva puni pristup isključivo kroz dodjelu Admin
/// starter GrantGroup-e (vidi AuthService.Register), ne kroz poseban autorizacijski put.
/// </summary>
public class GrantContext
{
    public GrantContext(HashSet<string> grants)
    {
        Grants = grants;
    }

    public HashSet<string> Grants { get; }

    public bool Has(string grant) => Grants.Contains(grant);

    public bool HasAny(params string[] grants) => grants.Any(Grants.Contains);
}