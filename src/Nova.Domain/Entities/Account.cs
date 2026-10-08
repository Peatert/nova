namespace Nova.Domain.Entities;

public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>20-значный номер счёта.</summary>
    public required string Number { get; set; }

    public AccountType Type { get; set; } = AccountType.Debit;
    public string Currency { get; set; } = "RUB";

    /// <summary>Текущий остаток. Источник истины — проводки (LedgerEntries); баланс — их кэш.</summary>
    public decimal Balance { get; private set; }

    /// <summary>Кредитный лимит. Для дебетового счёта 0: в минус уйти нельзя.</summary>
    public decimal CreditLimit { get; set; }

    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Оптимистическая блокировка (в PostgreSQL маппится на системный столбец xmin).</summary>
    public uint Version { get; set; }

    public List<Card> Cards { get; set; } = [];

    public void Debit(decimal amount)
    {
        EnsurePositive(amount);
        EnsureActive();
        if (Balance - amount < -CreditLimit)
            throw new DomainException("insufficient_funds", "Недостаточно средств");
        Balance -= amount;
    }

    public void Credit(decimal amount)
    {
        EnsurePositive(amount);
        if (Status == AccountStatus.Closed)
            throw new DomainException("account_closed", "Счёт закрыт");
        Balance += amount;
    }

    private void EnsureActive()
    {
        if (Status != AccountStatus.Active)
            throw new DomainException("account_inactive", "Счёт заморожен или закрыт");
    }

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("invalid_amount", "Сумма должна быть больше нуля");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("invalid_amount", "Не больше двух знаков после запятой");
    }
}
