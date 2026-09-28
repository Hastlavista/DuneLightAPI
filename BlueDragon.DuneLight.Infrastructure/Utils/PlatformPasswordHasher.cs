using System;
using System.Security.Cryptography;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>
/// PlatformAccount password hashing — deliberately NOT the tenant PasswordHasher (unsalted SHA-256, too weak
/// for a higher-privilege identity). PBKDF2-HMACSHA256 with a random salt per hash, stdlib only (no new
/// package). Stored as "{iterations}.{saltBase64}.{hashBase64}" so the iteration count and salt travel with
/// the hash and Verify never needs external state.
/// </summary>
public static class PlatformPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encodedHash)
    {
        string[] parts = encodedHash?.Split('.') ?? [];
        if (parts.Length != 3 || !int.TryParse(parts[0], out int iterations))
            return false;

        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] expectedHash = Convert.FromBase64String(parts[2]);
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
