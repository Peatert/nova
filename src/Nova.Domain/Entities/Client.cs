namespace Nova.Domain.Entities;

public class Client : IAuthSubject
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string FullName { get; set; }

    /// <summary>Телефон в формате E.164 (+79161234567). Уникален: по нему делаются переводы.</summary>
    public required string Phone { get; set; }

    public required string PasswordHash { get; set; }

    /// <summary>Паспорт в зашифрованном виде. Открытый текст в БД не хранится.</summary>
    public string? PassportEnc { get; set; }

    /// <summary>Секрет TOTP для 2FA, зашифрован.</summary>
    public string? TotpSecretEnc { get; set; }
    public bool TotpEnabled { get; set; }
    public long? TotpLastStep { get; set; }

    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Account> Accounts { get; set; } = [];
}
