using System.Security.Cryptography;
using Nova.Infrastructure.Security;

namespace Nova.Tests;

public class SecurityTests
{
    private readonly FieldEncryptor _enc = new(DevKeys.EncryptionKey);

    [Fact]
    public void Encrypt_then_decrypt_returns_original()
    {
        var secret = "9999 9912 3456 7890";
        Assert.Equal(secret, _enc.Decrypt(_enc.Encrypt(secret)));
    }

    [Fact]
    public void Same_plaintext_gives_different_ciphertext()
    {
        // Случайный nonce: по шифротексту нельзя понять, что у двух клиентов одинаковые данные.
        Assert.NotEqual(_enc.Encrypt("4512 345678"), _enc.Encrypt("4512 345678"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        var bytes = Convert.FromBase64String(_enc.Encrypt("4512 345678"));
        bytes[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => _enc.Decrypt(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void Wrong_key_cannot_decrypt()
    {
        var other = new FieldEncryptor(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var encrypted = _enc.Encrypt("секрет");
        Assert.ThrowsAny<CryptographicException>(() => other.Decrypt(encrypted));
    }

    [Fact]
    public void Password_hash_verifies_only_correct_password()
    {
        var hash = PasswordHasher.Hash("Nova-client-2026!");
        Assert.True(PasswordHasher.Verify("Nova-client-2026!", hash));
        Assert.False(PasswordHasher.Verify("nova-client-2026!", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Password_hashes_are_salted()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Theory]
    [InlineData("!")]
    [InlineData("garbage")]
    [InlineData("md5$1$x$y")]
    [InlineData("pbkdf2-sha256$100000$не-base64$!!")]
    public void Malformed_hash_never_verifies(string stored)
    {
        Assert.False(PasswordHasher.Verify("anything", stored));
    }
}
