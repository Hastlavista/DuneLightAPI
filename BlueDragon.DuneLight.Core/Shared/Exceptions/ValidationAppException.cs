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

    public IDictionary<string, string[]> Details { get; }
}
