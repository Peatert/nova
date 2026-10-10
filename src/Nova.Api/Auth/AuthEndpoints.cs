using Microsoft.EntityFrameworkCore;
using Nova.Domain;
using Nova.Domain.Cards;
using Nova.Domain.Clients;
using Nova.Domain.Entities;
using Nova.Domain.Security;
using Nova.Infrastructure.Audit;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;

namespace Nova.Api.Auth;

public static class AuthEndpoints
{
    private const string GenericLoginError = "Неверный логин или пароль";

    // Хеш-пустышка: если пользователя нет, всё равно тратим время на проверку пароля.
    // Иначе по времени ответа можно было бы понять, существует ли такой телефон или логин.
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash("nova-dummy-password-0"));

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting("auth");

        g.MapPost("/client/register", RegisterClient);
        g.MapPost("/client/login", LoginClient);
        g.MapPost("/staff/login", LoginStaff);
        g.MapPost("/2fa", VerifySecondFactor);
        g.MapPost("/refresh", Refresh);
        g.MapPost("/logout", Logout);
    }

    // ───────────────────────── Регистрация ─────────────────────────

    private static async Task<IResult> RegisterClient(
        ClientRegisterRequest req, NovaDbContext db, TokenService tokens, AuditWriter audit,
        FieldEncryptor enc, HttpContext http, CancellationToken ct)
    {
        if (!PhoneNumber.TryNormalize(req.Phone, out var phone))
            return Problem(400, "Некорректный номер телефона", "Нужен мобильный номер РФ: +7 9XX XXX-XX-XX");
        var fullName = req.FullName?.Trim() ?? "";
        if (fullName.Length is < 5 or > 200)
            return Problem(400, "Укажите фамилию, имя и отчество");
        if (PasswordPolicy.Validate(req.Password) is { } pwdError)
            return Problem(400, "Слабый пароль", pwdError);
        if (await db.Clients.AnyAsync(c => c.Phone == phone, ct))
            return Problem(409, "Этот номер уже зарегистрирован");

        var client = new Client { FullName = fullName, Phone = phone, PasswordHash = PasswordHasher.Hash(req.Password) };

        var seq = await db.Database
            .SqlQuery<long>($"SELECT nextval('accounts.account_number_seq') AS \"Value\"")
            .SingleAsync(ct);
        var account = new Account { ClientId = client.Id, Number = "40817810" + seq.ToString("D12"), Type = AccountType.Debit };

        var pan = NewPan();
        var exp = DateTime.UtcNow.AddYears(4);
        var card = new Card
        {
            AccountId = account.Id,
            PanEnc = enc.Encrypt(pan),
            PanLast4 = pan[^4..],
            ExpiresAt = new DateOnly(exp.Year, exp.Month, 1).AddMonths(1).AddDays(-1),
        };

        db.AddRange(client, account, card);
        var pair = tokens.IssuePair(Principal(client), mfa: false);
        await audit.WriteAsync(ActorType.Client, client.Id, "auth.register", PhoneNumber.Mask(phone), ip: Ip(http), ct: ct);

        return Results.Created("/api/me", pair);
    }

    // ───────────────────────── Вход ─────────────────────────

    private static async Task<IResult> LoginClient(
        ClientLoginRequest req, NovaDbContext db, TokenService tokens, AuditWriter audit,
        TimeProvider time, HttpContext http, CancellationToken ct)
    {
        Client? client = null;
        if (PhoneNumber.TryNormalize(req.Phone, out var phone))
            client = await db.Clients.SingleOrDefaultAsync(c => c.Phone == phone, ct);

        return await PasswordStep(client, ActorType.Client, req.Password, PhoneNumber.Mask(phone),
            () => Principal(client!), tokens, audit, time, http, ct);
    }

    private static async Task<IResult> LoginStaff(
        StaffLoginRequest req, NovaDbContext db, TokenService tokens, AuditWriter audit,
        TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var login = req.Login?.Trim().ToLowerInvariant() ?? "";
        var staff = await db.StaffMembers.Include(s => s.Role).SingleOrDefaultAsync(s => s.Login == login, ct);
        if (staff is { IsActive: false })
        {
            PasswordHasher.Verify(req.Password ?? "", DummyHash.Value);
            await audit.WriteAsync(ActorType.Staff, staff.Id, "auth.login.inactive", login, ip: Ip(http), ct: ct);
            return Problem(401, GenericLoginError);
        }

        return await PasswordStep(staff, ActorType.Staff, req.Password, login,
            () => Principal(staff!), tokens, audit, time, http, ct);
    }

    /// <summary>Общий шаг проверки пароля для клиента и сотрудника.</summary>
    private static async Task<IResult> PasswordStep(
        IAuthSubject? subject, ActorType type, string? password, string target, Func<AuthPrincipal> principal,
        TokenService tokens, AuditWriter audit, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        password ??= "";

        if (subject is null)
        {
            PasswordHasher.Verify(password, DummyHash.Value);
            await audit.WriteAsync(type, null, "auth.login.failed", target, new { reason = "unknown" }, Ip(http), ct);
            return Problem(401, GenericLoginError);
        }

        if (subject.IsLocked(now))
        {
            await audit.WriteAsync(type, subject.Id, "auth.login.blocked", target, ip: Ip(http), ct: ct);
            return Locked(subject, now);
        }

        if (!PasswordHasher.Verify(password, subject.PasswordHash))
        {
            var lockedNow = subject.RegisterFailure(now);
            await audit.WriteAsync(type, subject.Id, lockedNow ? "auth.locked" : "auth.login.failed", target,
                new { reason = "password" }, Ip(http), ct);
            return lockedNow ? Locked(subject, now) : Problem(401, GenericLoginError);
        }

        if (subject.TotpEnabled)
        {
            // Пароль верный, но нужен второй фактор. Счётчик ошибок пока не сбрасываем.
            await audit.WriteAsync(type, subject.Id, "auth.password.ok", target, ip: Ip(http), ct: ct);
            return Results.Ok(new MfaRequired(tokens.CreateMfaToken(type, subject.Id)));
        }

        subject.RegisterSuccess();
        var p = principal();
        var pair = tokens.IssuePair(p, mfa: false);
        await audit.WriteAsync(type, subject.Id, "auth.login.success", target,
            new { mfa = false, mfaSetupRequired = p.MfaSetupRequired }, Ip(http), ct);
        return Results.Ok(pair);
    }

    // ───────────────────────── Второй фактор ─────────────────────────

    private static async Task<IResult> VerifySecondFactor(
        TwoFactorRequest req, NovaDbContext db, TokenService tokens, AuditWriter audit,
        FieldEncryptor enc, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var who = await tokens.ValidateMfaTokenAsync(req.MfaToken ?? "");
        if (who is null) return Problem(401, "Сессия входа истекла, войдите заново");

        var (type, id) = who.Value;
        var subject = await LoadSubject(db, type, id, ct);
        if (subject is null || subject.TotpSecretEnc is null || !subject.TotpEnabled)
            return Problem(401, "Сессия входа истекла, войдите заново");

        var now = time.GetUtcNow();
        if (subject.IsLocked(now)) return Locked(subject, now);

        var secret = enc.Decrypt(subject.TotpSecretEnc);
        if (!Totp.Verify(secret, req.Code, now, subject.TotpLastStep, out var step))
        {
            var lockedNow = subject.RegisterFailure(now);
            await audit.WriteAsync(type, id, lockedNow ? "auth.locked" : "auth.2fa.failed", ip: Ip(http), ct: ct);
            return lockedNow ? Locked(subject, now) : Problem(401, "Неверный код");
        }

        subject.TotpLastStep = step;
        subject.RegisterSuccess();
        var pair = tokens.IssuePair(Principal(subject), mfa: true);
        await audit.WriteAsync(type, id, "auth.login.success", payload: new { mfa = true }, ip: Ip(http), ct: ct);
        return Results.Ok(pair);
    }

    // ───────────────────────── Refresh и выход ─────────────────────────

    private static async Task<IResult> Refresh(
        RefreshRequest req, NovaDbContext db, TokenService tokens, AuditWriter audit,
        TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(req.RefreshToken ?? "");
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        var now = time.GetUtcNow();
        if (rt is null) return Problem(401, "Сессия недействительна");

        if (rt.RevokedAt is not null)
        {
            // Отозванный токен предъявлен повторно — его украли (или клиент сломан). Гасим всю сессию.
            await RevokeFamily(db, rt.FamilyId, now, ct);
            await audit.WriteAsync(rt.SubjectType, rt.SubjectId, "auth.refresh.reuse_detected",
                payload: new { family = rt.FamilyId }, ip: Ip(http), ct: ct);
            return Problem(401, "Сессия недействительна");
        }
        if (rt.ExpiresAt <= now) return Problem(401, "Сессия истекла");

        var subject = await LoadSubject(db, rt.SubjectType, rt.SubjectId, ct);
        if (subject is null || subject.IsLocked(now) || subject is Staff { IsActive: false })
        {
            await RevokeFamily(db, rt.FamilyId, now, ct);
            await audit.WriteAsync(rt.SubjectType, rt.SubjectId, "auth.refresh.denied", ip: Ip(http), ct: ct);
            return Problem(401, "Сессия недействительна");
        }

        // Права пересчитываются при каждом обновлении: если роль сменили, через 15 минут это вступит в силу.
        var pair = tokens.Rotate(rt, Principal(subject));
        await db.SaveChangesAsync(ct);
        return Results.Ok(pair);
    }

    private static async Task<IResult> Logout(
        RefreshRequest req, NovaDbContext db, AuditWriter audit, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(req.RefreshToken ?? "");
        var rt = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is not null)
        {
            await RevokeFamily(db, rt.FamilyId, time.GetUtcNow(), ct);
            await audit.WriteAsync(rt.SubjectType, rt.SubjectId, "auth.logout", ip: Ip(http), ct: ct);
        }
        return Results.NoContent(); // одинаковый ответ, даже если токен неизвестен
    }

    // ───────────────────────── Вспомогательное ─────────────────────────

    internal static async Task<IAuthSubject?> LoadSubject(NovaDbContext db, ActorType type, Guid id, CancellationToken ct) =>
        type == ActorType.Client
            ? await db.Clients.SingleOrDefaultAsync(c => c.Id == id, ct)
            : await db.StaffMembers.Include(s => s.Role).SingleOrDefaultAsync(s => s.Id == id, ct);

    internal static AuthPrincipal Principal(IAuthSubject subject) => subject switch
    {
        Client c => new AuthPrincipal(ActorType.Client, c.Id, c.FullName, null, [], false),
        Staff s => StaffPrincipal(s),
        _ => throw new InvalidOperationException("Неизвестный тип пользователя"),
    };

    private static AuthPrincipal StaffPrincipal(Staff s)
    {
        var role = s.Role ?? throw new InvalidOperationException("Роль сотрудника не загружена");
        // Роли с обязательной 2FA без настроенного второго фактора получают пустой список прав.
        var setupRequired = Roles.RequireMfa.Contains(role.Code) && !s.TotpEnabled;
        return new AuthPrincipal(ActorType.Staff, s.Id, s.FullName, role.Code,
            setupRequired ? [] : role.Permissions, setupRequired);
    }

    private static async Task RevokeFamily(NovaDbContext db, Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.RevokedAt = now;
    }

    private static IResult Locked(IAuthSubject s, DateTimeOffset now)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((s.LockedUntil!.Value - now).TotalMinutes));
        return Problem(423, "Вход временно заблокирован",
            $"Слишком много неудачных попыток. Повторите через {minutes} мин.");
    }

    internal static IResult Problem(int status, string title, string? detail = null) =>
        Results.Problem(title: title, detail: detail, statusCode: status);

    internal static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static string NewPan()
    {
        Span<char> d = stackalloc char[16];
        "999999".AsSpan().CopyTo(d);
        for (var k = 6; k < 15; k++) d[k] = (char)('0' + Random.Shared.Next(10));
        d[15] = (char)('0' + Luhn.CheckDigit(d[..15]));
        return new string(d);
    }
}
