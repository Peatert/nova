namespace Nova.Domain.Entities;

/// <summary>
/// Проводка двойной записи. Каждый перевод = ровно две проводки (Debit + Credit) на одну сумму.
/// Записи только добавляются, никогда не изменяются и не удаляются.
/// </summary>
public class LedgerEntry
{
    public long Id { get; set; }
    public Guid TransferId { get; set; }
    public Guid AccountId { get; set; }
    public EntryDirection Direction { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Остаток счёта после проводки — для быстрых выписок без пересчёта.</summary>
    public decimal BalanceAfter { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
