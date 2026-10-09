namespace BlueDragon.DuneLight.Core.Enums;

/// <summary>T1 — vrsta odbijanja 403 (<c>error.details.reason</c>), da frontend prikaže pravu poruku (FE-ADR-0003).</summary>
public enum ForbiddenReason
{
    /// <summary>Nedostaje grant (ili više njih) za radnju; popis u <c>requiredGrants</c>, način u <c>match</c>.</summary>
    MissingGrant,

    /// <summary>Korisnik ima own opseg, a resurs nije njegov; potreban je opseg all (<c>requiredGrants</c>).</summary>
    OutOfScope,

    /// <summary>Korisnik bez granta pristupa poslovnici kojoj nije dodijeljen (<c>companyId</c>).</summary>
    CompanyNotAssigned
}
