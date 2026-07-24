using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Baca se kad traženi zapis ne postoji (404).</summary>
public class NotFoundAppException : Exception
{
    public NotFoundAppException(string entityName, object id)
        : base($"{entityName} with id '{id}' was not found.")
    {
    }
}
