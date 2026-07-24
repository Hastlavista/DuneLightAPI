using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Baca se kad prijava/autentikacija ne uspije zbog poslovnog razloga (krivi podaci, kriva lozinka) — mapira se na 401.</summary>
public class UnauthorizedAppException : Exception
{
    public UnauthorizedAppException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
