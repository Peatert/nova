namespace Nova.Domain.Entities;

public class Transfer
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Ключ от клиента: повторный запрос с тем же ключом не спишет деньги второй раз.</summary>
    public Guid IdempotencyKey { get; set; }

    public Guid FromAccountId { get; set; }
    public Guid ToAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "RUB";
    public string? Comment { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public string? FailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<LedgerEntry> Entries { get; set; } = [];
}
