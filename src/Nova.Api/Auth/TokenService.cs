using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nova.Domain;
using Nova.Domain.Entities;
using Nova.Infrastructure.Persistence;

namespace Nova.Api.Auth;

/// <summary>
/// Выдача токенов.
///  • Access — JWT на 15 минут, в нём права. Сервер его не хранит, проверяет только подпись.
///  • Refresh — случайные 256 бит на 30 дней, в БД лежит только SHA-256. Ротация при каждом обновлении.
///  • MFA — JWT на 5 минут с другой аудиторией: годится только для ввода кода 2FA, не для API.
///
/// Методы только ДОБАВЛЯЮТ refresh-токен в контекст. Сохраняет вызывающий код (через AuditWriter),
/// чтобы токен и запись аудита попали в БД одной транзакцией.
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options, NovaDbContext db, TimeProvider time)
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly JwtOptions _o = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Convert.FromBase64String(o.SigningKey));
    private string MfaAudience => _o.Audience + "-mfa";

    public TokenPair IssuePair(AuthPrincipal p, bool mfa, Guid? familyId = null)
    {
        var access = CreateAccessToken(p, mfa);
        var (refresh, _) = AddRefreshToken(p.Type, p.Id, familyId ?? Guid.CreateVersion7(), mfa);
        return new TokenPair(access, refresh, _o.AccessTokenMinutes * 60, p.MfaSetupRequired);
    }

    /// <summary>Ротация: новый токен в том же семействе, старый помечается заменённым.</summary>
    public TokenPair Rotate(RefreshToken old, AuthPrincipal p)
    {
        var access = CreateAccessToken(p, old.Mfa);
        var (refresh, entity) = AddRefreshToken(p.Type, p.Id, old.FamilyId, old.Mfa);
        old.RevokedAt = time.GetUtcNow();
        old.ReplacedById = entity.Id;
        return new TokenPair(access, refresh, _o.AccessTokenMinutes * 60, p.MfaSetupRequired);
    }

    public string CreateAccessToken(AuthPrincipal p, bool mfa)
    {
        var claims = new List<Claim>
        {
            new(NovaClaims.Subject, p.Id.ToString()),
            new(NovaClaims.SubjectType, p.Type == ActorType.Client ? "client" : "staff"),
            new(NovaClaims.Name, p.Name),
            new(NovaClaims.AuthMethod, mfa ? "mfa" : "pwd"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (p.RoleCode is not null) claims.Add(new(NovaClaims.Role, p.RoleCode));
        if (p.MfaSetupRequired) claims.Add(new(NovaClaims.MfaSetupRequired, "true"));
        claims.AddRange(p.Permissions.Select(perm => new Claim(NovaClaims.Permission, perm)));

        return CreateJwt(claims, _o.Audience, TimeSpan.FromMinutes(_o.AccessTokenMinutes));
    }

    public string CreateMfaToken(ActorType type, Guid id) =>
        CreateJwt(
            [
                new(NovaClaims.Subject, id.ToString()),
                new(NovaClaims.SubjectType, type == ActorType.Client ? "client" : "staff"),
            ],
            MfaAudience, TimeSpan.FromMinutes(_o.MfaTokenMinutes));

    /// <summary>Проверяет MFA-токен. Возвращает (тип, id) или null.</summary>
    public async Task<(ActorType Type, Guid Id)?> ValidateMfaTokenAsync(string token)
    {
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _o.Issuer,
            ValidAudience = MfaAudience,
            IssuerSigningKey = SigningKey(_o),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        });
        if (!result.IsValid) return null;

        var sub = result.ClaimsIdentity.FindFirst(NovaClaims.Subject)?.Value;
        var typ = result.ClaimsIdentity.FindFirst(NovaClaims.SubjectType)?.Value;
        if (!Guid.TryParse(sub, out var id)) return null;
        return (typ == "client" ? ActorType.Client : ActorType.Staff, id);
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private (string Token, RefreshToken Entity) AddRefreshToken(ActorType type, Guid subjectId, Guid familyId, bool mfa)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        var entity = new RefreshToken
        {
            SubjectType = type,
            SubjectId = subjectId,
            TokenHash = HashRefreshToken(token),
            FamilyId = familyId,
            Mfa = mfa,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_o.RefreshTokenDays),
        };
        db.RefreshTokens.Add(entity);
        return (token, entity);
    }

    private string CreateJwt(IEnumerable<Claim> claims, string audience, TimeSpan lifetime)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _o.Issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(lifetime),
            SigningCredentials = new SigningCredentials(SigningKey(_o), SecurityAlgorithms.HmacSha256),
        });
    }
}
