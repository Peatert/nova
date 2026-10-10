using Nova.Domain;

namespace Nova.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "nova";
    public string Audience { get; set; } = "nova-api";
    /// <summary>Ключ подписи HMAC-SHA256 в base64, не короче 32 байт.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int MfaTokenMinutes { get; set; } = 5;
}

/// <summary>Имена claims в токене. Короткие и без маппинга Microsoft — читаются так же, как записаны.</summary>
public static class NovaClaims
{
    public const string Subject = "sub";
    public const string SubjectType = "sub_type"; // client | staff
    public const string Name = "name";
    public const string Role = "role";
    public const string Permission = "perm";
    /// <summary>Как подтверждена личность: pwd — только пароль, mfa — пароль + код.</summary>
    public const string AuthMethod = "amr";
    /// <summary>Выставляется, когда роли нужна 2FA, а она ещё не настроена. Прав в таком токене нет.</summary>
    public const string MfaSetupRequired = "mfa_setup";
}

/// <summary>Кто вошёл: из этого строится access-токен.</summary>
public sealed record AuthPrincipal(
    ActorType Type, Guid Id, string Name, string? RoleCode, IReadOnlyList<string> Permissions, bool MfaSetupRequired);

public sealed record TokenPair(
    string AccessToken, string RefreshToken, int ExpiresIn, bool MfaSetupRequired, string TokenType = "Bearer");

public sealed record MfaRequired(string MfaToken, bool Requires2fa = true);

public sealed record ClientRegisterRequest(string Phone, string FullName, string Password);
public sealed record ClientLoginRequest(string Phone, string Password);
public sealed record StaffLoginRequest(string Login, string Password);
public sealed record TwoFactorRequest(string MfaToken, string Code);
public sealed record RefreshRequest(string RefreshToken);
public sealed record CodeRequest(string Code);
