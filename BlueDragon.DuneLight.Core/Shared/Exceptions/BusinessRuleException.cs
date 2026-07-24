using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>
/// Baca se kad zahtjev krši poslovno pravilo (preklapanje cijena, zadnja aktivna lokacija,
/// pokušaj brisanja referenciranog zapisa, itd.) — mapira se na 409.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
