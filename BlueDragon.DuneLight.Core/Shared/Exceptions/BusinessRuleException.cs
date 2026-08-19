using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>
/// Baca se kad zahtjev krši poslovno pravilo (preklapanje cijena, zadnja aktivna tvrtka,
/// pokušaj brisanja referenciranog zapisa, itd.) — mapira se na 409.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message, object details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    /// <summary>Opcionalan strukturirani prilog uz kod/poruku (npr. RECURRING_CONFLICT nosi { conflicts: [...] }). Null za sva ostala poslovna pravila.</summary>
    public object Details { get; }
}
