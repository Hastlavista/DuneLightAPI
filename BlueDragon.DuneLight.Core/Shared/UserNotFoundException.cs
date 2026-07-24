using System;

namespace BlueDragon.DuneLight.Core.Shared;

public class UserNotFoundException : Exception
{
    public UserNotFoundException() : base("User not found.")
    {
    }
}
