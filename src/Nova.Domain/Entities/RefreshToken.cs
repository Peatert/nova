namespace Nova.Domain.Entities;

/// <summary>
/// Refresh-токен с ротацией. Каждое обновление выдаёт новый токен и отзывает старый.
/// Все токены одной сессии входа образуют «семейство» (FamilyId).
/// Если кто-то предъявит уже отозванный токен — значит, токен украли: отзываем всё семейство.
/// В БД хранится только SHA-256 токена, сам токен знает лишь клиент.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public ActorType SubjectType { get; set; }
    public Guid SubjectId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }

    /// <summary>Сессия начата со вторым фактором — признак переносится на обновлённые токены.</summary>
    public bool Mfa { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
