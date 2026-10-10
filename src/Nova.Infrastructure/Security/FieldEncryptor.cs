using System.Security.Cryptography;
using System.Text;

namespace Nova.Infrastructure.Security;

/// <summary>
/// Шифрование чувствительных полей (номер карты, паспорт, секрет 2FA) алгоритмом AES-256-GCM.
/// Формат: base64( nonce[12] | tag[16] | ciphertext ).
/// GCM даёт и конфиденциальность, и целостность: подменённый шифротекст не расшифруется.
/// </summary>
public sealed class FieldEncryptor
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public FieldEncryptor(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
            throw new ArgumentException("Ключ шифрования должен быть 32 байта (AES-256) в base64");
    }

    public string Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plain.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(buffer);
    }

    public string Decrypt(string encrypted)
    {
        var buffer = Convert.FromBase64String(encrypted);
        if (buffer.Length < NonceSize + TagSize)
            throw new CryptographicException("Повреждённые данные");
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public static class DevKeys
{
    /// <summary>
    /// Ключ ТОЛЬКО для локальной разработки и тестовых данных. В проде ключ берётся из
    /// хранилища секретов (переменная окружения / Vault), а этот константный ключ не используется.
    /// </summary>
    public const string EncryptionKey = "mMchrG3Gvc6BS9vjngzl0P1nFIR3ttHWkGszlV1TZz4=";
}
