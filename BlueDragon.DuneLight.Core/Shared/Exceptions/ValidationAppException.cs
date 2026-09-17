using System;
using System.Collections.Generic;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Baca se kad ulazni podaci ne zadovoljavaju poslovna validacijska pravila (400).</summary>
public class ValidationAppException : Exception
{
    public ValidationAppException(string message, IDictionary<string, string[]> details = null) : base(message)
    {
        Details = details;
    }

    /// <summary>Za domenski specifičnu 400 grešku koja treba stabilan code umjesto generičkog VALIDATION_ERROR
    /// (npr. PAYMENT_VOID_REASON_REQUIRED) — vidi ErrorCodes.cs.</summary>
    public ValidationAppException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>Null znači "generička VALIDATION_ERROR" — ExceptionHandlingMiddleware pada natrag na to.</summary>
    public string Code { get; }

    public IDictionary<string, string[]> Details { get; }
}
