using System;
using System.Security.Cryptography;
using System.Text;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }
}
