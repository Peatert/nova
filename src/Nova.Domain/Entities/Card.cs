namespace Nova.Domain.Entities;

public class Card
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>Номер карты, зашифрован. Сотрудники видят только PanLast4.</summary>
    public required string PanEnc { get; set; }
    public required string PanLast4 { get; set; }

    public DateOnly ExpiresAt { get; set; }
    public CardStatus Status { get; set; } = CardStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
