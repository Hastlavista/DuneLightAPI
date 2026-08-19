using System.Collections.Generic;
using System.Linq;

namespace BlueDragon.DuneLight.Core.Shared;

/// <summary>Rezultat razrješavanja efektivnih ovlasti korisnika za jedan zahtjev — Owner uvijek prolazi, bez obzira na Grants.</summary>
public class GrantContext
{
    public GrantContext(bool isOwner, HashSet<string> grants)
    {
        IsOwner = isOwner;
        Grants = grants;
    }

    public bool IsOwner { get; }
    public HashSet<string> Grants { get; }

    public bool Has(string grant) => IsOwner || Grants.Contains(grant);

    public bool HasAny(params string[] grants) => IsOwner || grants.Any(Grants.Contains);
}