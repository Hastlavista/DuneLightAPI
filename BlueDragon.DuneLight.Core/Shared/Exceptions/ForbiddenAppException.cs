using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Phase M1F — autentificiran pozivatelj nema raw grant za EKSPLICITNO zatraženu povlasticu unutar inače dopuštene
/// operacije (npr. prekoračenje mekog kapaciteta grupe bez groups.capacity.override). API: 403 FORBIDDEN.</summary>
public class ForbiddenAppException : Exception
{
    public string Code { get; }

    public ForbiddenAppException(string message, string code = null) : base(message)
    {
        Code = code ?? ErrorCodes.Forbidden;
    }
}
