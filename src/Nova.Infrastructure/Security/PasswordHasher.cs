using System.Security.Cryptography;

namespace Nova.Infrastructure.Security;

/// <summary>
/// Хеширование паролей PBKDF2-HMAC-SHA256 с солью.
/// Формат: pbkdf2-sha256$итерации$соль(base64)$хеш(base64). Число итераций хранится в строке,
/// поэтому его можно поднять позже без поломки старых паролей.
/// </summary>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
            return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (iterations < 1 || expected.Length == 0) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        // Сравнение за постоянное время: не даёт подбирать хеш по времени ответа.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
