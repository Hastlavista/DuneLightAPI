using System;

namespace BlueDragon.DuneLight.Core.Shared.Exceptions;

/// <summary>Baca se kad traženi zapis ne postoji (404).</summary>
public class NotFoundAppException : Exception
{
    public NotFoundAppException(string entityName, object id)
        : base($"{entityName} with id '{id}' was not found.")
    {
    }

    /// <summary>Domenski specifičan 404 s vlastitim stabilnim kodom
    /// umjesto generičkog NOT_FOUND, isti idiom kao ValidationAppException(code, message). Null Code (postojeći
    /// jednoparametarski konstruktor gore) i dalje pada natrag na ErrorCodes.NotFound u middleware-u — postojeći
    /// pozivi ostaju nepromijenjeni.</summary>
    public NotFoundAppException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
