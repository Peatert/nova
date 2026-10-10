using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Nova.Infrastructure.Security;

/// <summary>
/// Одноразовые коды по времени (TOTP, RFC 6238) — то, что показывают Google Authenticator,
/// Яндекс Ключ и другие приложения-аутентификаторы. HMAC-SHA1, 6 цифр, шаг 30 секунд.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    private const int SecretBytes = 20; // 160 бит — рекомендация RFC 4226

    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Compute(byte[] key, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(key, counter, hash);

        // Динамическое усечение (RFC 4226, раздел 5.3).
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];
        var mod = (int)Math.Pow(10, digits);
        return (binary % mod).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Проверяет код с допуском ±1 шаг (часы телефона могут отставать на десятки секунд).
    /// Код с шагом не новее lastUsedStep отклоняется: перехваченный код нельзя использовать повторно.
    /// </summary>
    public static bool Verify(string base32Secret, string? code, DateTimeOffset now, long? lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (code is null) return false;
        code = code.Replace(" ", "");
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return false;

        var key = Base32.Decode(base32Secret);
        var current = StepAt(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (lastUsedStep is { } last && step <= last) continue;
            var expected = Compute(key, step);
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.ASCII.GetBytes(expected), System.Text.Encoding.ASCII.GetBytes(code)))
            {
                matchedStep = step;
                return true;
            }
        }
        return false;
    }

    /// <summary>Ссылка otpauth:// — из неё строится QR-код для приложения-аутентификатора.</summary>
    public static string BuildUri(string issuer, string account, string base32Secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}

/// <summary>Base32 (RFC 4648) без паддинга — формат секретов TOTP.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        text = text.TrimEnd('=').Replace(" ", "").ToUpperInvariant();
        var result = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in text)
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException($"Недопустимый символ Base32: '{ch}'");
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                result.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. result];
    }
}
