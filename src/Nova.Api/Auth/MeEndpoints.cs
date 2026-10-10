using System.Security.Claims;
using Nova.Domain;
using Nova.Infrastructure.Audit;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;
using QRCoder;

namespace Nova.Api.Auth;

/// <summary>Профиль текущего пользователя и настройка 2FA. Доступно любому вошедшему, в том числе без прав.</summary>
public static class MeEndpoints
{
    private const string Issuer = "Nova";

    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/me").WithTags("Me").RequireAuthorization();

        g.MapGet("", (ClaimsPrincipal user) => Results.Ok(new
        {
            Id = user.FindFirstValue(NovaClaims.Subject),
            Type = user.FindFirstValue(NovaClaims.SubjectType),
            Name = user.FindFirstValue(NovaClaims.Name),
            Role = user.FindFirstValue(NovaClaims.Role),
            AuthMethod = user.FindFirstValue(NovaClaims.AuthMethod),
            MfaSetupRequired = user.HasClaim(NovaClaims.MfaSetupRequired, "true"),
            Permissions = user.FindAll(NovaClaims.Permission).Select(c => c.Value).Order().ToArray(),
        }));

        g.MapPost("/2fa/setup", SetupTwoFactor);
        g.MapPost("/2fa/confirm", ConfirmTwoFactor);
    }

    /// <summary>Шаг 1: создаём секрет и отдаём QR-код. 2FA ещё не включена, пока не подтверждён код.</summary>
    private static async Task<IResult> SetupTwoFactor(
        ClaimsPrincipal user, NovaDbContext db, FieldEncryptor enc, CancellationToken ct)
    {
        var (type, id) = Who(user);
        var subject = await AuthEndpoints.LoadSubject(db, type, id, ct);
        if (subject is null) return Results.Unauthorized();
        if (subject.TotpEnabled)
            return AuthEndpoints.Problem(409, "2FA уже включена");

        // Повторный вызов отдаёт тот же неподтверждённый секрет, а не новый.
        // Иначе QR, уже отсканированный в телефоне, молча перестал бы подходить.
        string secret;
        if (subject.TotpSecretEnc is not null)
        {
            secret = enc.Decrypt(subject.TotpSecretEnc);
        }
        else
        {
            secret = Totp.GenerateSecret();
            subject.TotpSecretEnc = enc.Encrypt(secret);
            subject.TotpLastStep = null;
            await db.SaveChangesAsync(ct);
        }

        var account = user.FindFirstValue(NovaClaims.Name) ?? id.ToString();
        var uri = Totp.BuildUri(Issuer, account, secret);
        using var qrData = new QRCodeGenerator().CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(qrData).GetGraphic(6);

        return Results.Ok(new
        {
            Secret = secret,
            OtpauthUri = uri,
            // Вставь эту строку в адресную строку браузера — откроется QR-код для сканирования.
            QrCode = "data:image/png;base64," + Convert.ToBase64String(png),
            Next = "Отсканируй QR в приложении-аутентификаторе и отправь код из него на /api/me/2fa/confirm",
        });
    }

    /// <summary>Шаг 2: проверяем первый код. Только после этого 2FA включается.</summary>
    private static async Task<IResult> ConfirmTwoFactor(
        CodeRequest req, ClaimsPrincipal user, NovaDbContext db, FieldEncryptor enc,
        AuditWriter audit, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var (type, id) = Who(user);
        var subject = await AuthEndpoints.LoadSubject(db, type, id, ct);
        if (subject is null) return Results.Unauthorized();
        if (subject.TotpEnabled) return AuthEndpoints.Problem(409, "2FA уже включена");
        if (subject.TotpSecretEnc is null) return AuthEndpoints.Problem(400, "Сначала вызови /api/me/2fa/setup");

        var secret = enc.Decrypt(subject.TotpSecretEnc);
        if (!Totp.Verify(secret, req.Code, time.GetUtcNow(), null, out var step))
            return AuthEndpoints.Problem(400, "Неверный код", "Проверь, что время на телефоне выставлено автоматически");

        subject.TotpEnabled = true;
        subject.TotpLastStep = step;
        await audit.WriteAsync(type, id, "auth.2fa.enabled", ip: AuthEndpoints.Ip(http), ct: ct);

        return Results.Ok(new
        {
            Enabled = true,
            Next = user.HasClaim(NovaClaims.MfaSetupRequired, "true")
                ? "2FA включена. Войди заново (пароль + код), чтобы получить права своей роли."
                : "2FA включена. При следующем входе понадобится код.",
        });
    }

    private static (ActorType Type, Guid Id) Who(ClaimsPrincipal user) =>
        (user.FindFirstValue(NovaClaims.SubjectType) == "client" ? ActorType.Client : ActorType.Staff,
         Guid.Parse(user.FindFirstValue(NovaClaims.Subject)!));
}
