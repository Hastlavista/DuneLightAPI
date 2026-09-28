using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests;

/// <summary>PBKDF2-based hasher used exclusively for PlatformAccount - deliberately NOT the tenant
/// PasswordHasher (unsalted SHA-256), see PlatformPasswordHasher's own doc comment.</summary>
public class PlatformPasswordHasherTests
{
    [Fact]
    public void Hash_then_Verify_with_the_correct_password_succeeds()
    {
        string hash = PlatformPasswordHasher.Hash("correct horse battery staple");

        Assert.True(PlatformPasswordHasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_with_the_wrong_password_fails()
    {
        string hash = PlatformPasswordHasher.Hash("correct horse battery staple");

        Assert.False(PlatformPasswordHasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes_due_to_random_salt()
    {
        string hash1 = PlatformPasswordHasher.Hash("same password");
        string hash2 = PlatformPasswordHasher.Hash("same password");

        Assert.NotEqual(hash1, hash2);
        Assert.True(PlatformPasswordHasher.Verify("same password", hash1));
        Assert.True(PlatformPasswordHasher.Verify("same password", hash2));
    }

    [Fact]
    public void Verify_against_a_malformed_hash_fails_instead_of_throwing()
    {
        Assert.False(PlatformPasswordHasher.Verify("anything", "not-a-valid-hash"));
    }
}
