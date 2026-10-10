namespace Nova.Domain.Entities;

/// <summary>Тот, кто входит в систему по паролю: клиент или сотрудник.</summary>
public interface IAuthSubject
{
    Guid Id { get; }
    string PasswordHash { get; set; }

    int FailedLoginCount { get; set; }
    DateTimeOffset? LockedUntil { get; set; }

    /// <summary>2FA включена (секрет подтверждён кодом).</summary>
    bool TotpEnabled { get; set; }
    /// <summary>Секрет TOTP, зашифрован. Пока TotpEnabled = false — это ещё не подтверждённый секрет.</summary>
    string? TotpSecretEnc { get; set; }
    /// <summary>Номер последнего принятого 30-секундного шага: один и тот же код нельзя использовать дважды.</summary>
    long? TotpLastStep { get; set; }
}

/// <summary>Правила блокировки при подборе пароля.</summary>
public static class LoginPolicy
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static bool IsLocked(this IAuthSubject s, DateTimeOffset now) =>
        s.LockedUntil is { } until && until > now;

    /// <summary>Учитывает неудачную попытку. Возвращает true, если учётная запись только что заблокирована.</summary>
    public static bool RegisterFailure(this IAuthSubject s, DateTimeOffset now)
    {
        s.FailedLoginCount++;
        if (s.FailedLoginCount < MaxFailedAttempts) return false;

        s.LockedUntil = now + LockoutDuration;
        s.FailedLoginCount = 0;
        return true;
    }

    public static void RegisterSuccess(this IAuthSubject s)
    {
        s.FailedLoginCount = 0;
        s.LockedUntil = null;
    }
}
