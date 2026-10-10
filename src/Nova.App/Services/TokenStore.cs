using System.Text;
using System.Text.Json;
#if WINDOWS
using System.Security.Cryptography;
#endif

namespace Nova.App.Services;

/// <summary>
/// Хранение сессии на устройстве.
///  • Android — SecureStorage: ключ лежит в аппаратном Android Keystore, достать его из файлов нельзя.
///  • Windows — файл, зашифрованный DPAPI ключом учётной записи Windows.
/// Пароль не хранится никогда — только токены.
/// </summary>
public sealed class TokenStore
{
    private const string Key = "nova.session";
    private bool _loaded;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public bool HasSession => RefreshToken is not null;

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var json = await ReadAsync();
            if (json is null) return;
            var s = JsonSerializer.Deserialize<Stored>(json);
            AccessToken = s?.A;
            RefreshToken = s?.R;
        }
        catch
        {
            // Повреждённое хранилище (например, после переустановки) — просто попросим войти заново.
            AccessToken = RefreshToken = null;
        }
    }

    public async Task SaveAsync(string accessToken, string refreshToken)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        _loaded = true;
        await WriteAsync(JsonSerializer.Serialize(new Stored(accessToken, refreshToken)));
    }

    public async Task ClearAsync()
    {
        AccessToken = RefreshToken = null;
        try { await DeleteAsync(); } catch { /* нечего удалять */ }
    }

    private sealed record Stored(string A, string R);

#if WINDOWS
    private static string FilePath
    {
        get
        {
            Directory.CreateDirectory(FileSystem.AppDataDirectory);
            return Path.Combine(FileSystem.AppDataDirectory, "session.bin");
        }
    }

    private static Task<string?> ReadAsync()
    {
        if (!File.Exists(FilePath)) return Task.FromResult<string?>(null);
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
        return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
    }

    private static Task WriteAsync(string value)
    {
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, cipher);
        return Task.CompletedTask;
    }

    private static Task DeleteAsync()
    {
        File.Delete(FilePath);
        return Task.CompletedTask;
    }
#else
    private static Task<string?> ReadAsync() => SecureStorage.Default.GetAsync(Key);

    private static Task WriteAsync(string value) => SecureStorage.Default.SetAsync(Key, value);

    private static Task DeleteAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
#endif
}
