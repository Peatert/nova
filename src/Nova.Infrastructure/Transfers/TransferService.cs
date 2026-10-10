using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nova.Domain;
using Nova.Domain.Entities;
using Nova.Infrastructure.Persistence;

namespace Nova.Infrastructure.Transfers;

/// <param name="InitiatorClientId">Клиент, от имени которого перевод. null — системная операция (зачисление, тесты).</param>
public sealed record TransferCommand(
    Guid FromAccountId, Guid ToAccountId, decimal Amount, string? Comment, Guid IdempotencyKey, Guid? InitiatorClientId);

public sealed record TransferOutcome(Transfer Transfer, bool Created, decimal FromBalanceAfter);

/// <summary>
/// Перевод между счетами Nova. Гарантии:
///
///  1. Атомарность: списание, зачисление и обе проводки — одна транзакция. Либо всё, либо ничего.
///  2. Никаких гонок: оба счёта блокируются (SELECT … FOR UPDATE) до проверки баланса.
///     Два параллельных перевода с одного счёта выполняются строго по очереди, баланс не уйдёт в минус.
///  3. Никаких дедлоков: счета блокируются всегда в одном порядке (по возрастанию id).
///     Встречные переводы A→B и B→A не могут захватить блокировки крест-накрест.
///  4. Идемпотентность: повтор запроса с тем же ключом возвращает уже сделанный перевод, деньги не списываются дважды.
///     Даже если два одинаковых запроса пришли одновременно — второй упрётся в уникальный индекс и вернёт первый.
/// </summary>
public sealed class TransferService(NovaDbContext db, TimeProvider time)
{
    /// <summary>Максимум одного перевода другому клиенту.</summary>
    public const decimal MaxSingleTransfer = 300_000m;
    /// <summary>Максимум переводов другим клиентам за сутки (по Москве) с одного счёта.</summary>
    public const decimal DailyLimitToOthers = 1_000_000m;

    private static readonly TimeSpan Moscow = TimeSpan.FromHours(3);

    public async Task<TransferOutcome> ExecuteAsync(TransferCommand cmd, CancellationToken ct = default)
    {
        if (cmd.FromAccountId == cmd.ToAccountId)
            throw new DomainException("same_account", "Нельзя перевести на тот же счёт");
        if (cmd.Comment is { Length: > 140 })
            throw new DomainException("comment_too_long", "Комментарий — не длиннее 140 символов");

        // Быстрый путь: такой перевод уже был.
        if (await FindExistingAsync(cmd, ct) is { } done)
            return done;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Блокировки строго по возрастанию id — защита от дедлока при встречных переводах.
        Account? from = null, to = null;
        foreach (var id in new[] { cmd.FromAccountId, cmd.ToAccountId }.Order())
        {
            // xmin перечисляем явно: это системный столбец, в a.* он не входит, а EF ждёт его как версию строки.
            // Без LINQ поверх FromSql, чтобы EF не обернул FOR UPDATE в подзапрос.
            var rows = await db.Accounts
                .FromSql($"SELECT a.*, a.xmin FROM accounts.accounts a WHERE a.id = {id} FOR UPDATE")
                .ToListAsync(ct);
            var acc = rows.SingleOrDefault();
            if (id == cmd.FromAccountId) from = acc; else to = acc;
        }

        // Чужой счёт и несуществующий счёт дают одинаковую ошибку: нельзя перебором узнать чужие id.
        if (from is null || (cmd.InitiatorClientId is { } owner && from.ClientId != owner))
            throw new DomainException("account_not_found", "Счёт списания не найден");
        if (to is null)
            throw new DomainException("recipient_not_found", "Счёт получателя не найден");
        if (from.Currency != to.Currency)
            throw new DomainException("currency_mismatch", "Валюты счетов не совпадают");

        var now = time.GetUtcNow();
        if (cmd.InitiatorClientId is not null && to.ClientId != from.ClientId)
            await CheckLimitsAsync(from, cmd.Amount, now, ct);

        // Доменные правила: сумма > 0, не больше 2 знаков, счёт активен, хватает денег с учётом кредитного лимита.
        from.Debit(cmd.Amount);
        to.Credit(cmd.Amount);

        var transfer = new Transfer
        {
            Id = Guid.CreateVersion7(now),
            IdempotencyKey = cmd.IdempotencyKey,
            FromAccountId = from.Id,
            ToAccountId = to.Id,
            Amount = cmd.Amount,
            Currency = from.Currency,
            Comment = string.IsNullOrWhiteSpace(cmd.Comment) ? null : cmd.Comment.Trim(),
            Status = TransferStatus.Completed,
            CreatedAt = now,
        };
        transfer.Entries.Add(new LedgerEntry
        {
            TransferId = transfer.Id, AccountId = from.Id, Direction = EntryDirection.Debit,
            Amount = cmd.Amount, BalanceAfter = from.Balance, CreatedAt = now,
        });
        transfer.Entries.Add(new LedgerEntry
        {
            TransferId = transfer.Id, AccountId = to.Id, Direction = EntryDirection.Credit,
            Amount = cmd.Amount, BalanceAfter = to.Balance, CreatedAt = now,
        });
        db.Transfers.Add(transfer);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new TransferOutcome(transfer, Created: true, from.Balance);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_transfers_idempotency_key" })
        {
            // Параллельный запрос с тем же ключом успел первым. Откатываемся и отдаём его результат.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await FindExistingAsync(cmd, ct)
                   ?? throw new DomainException("idempotency_conflict", "Повторите запрос");
        }
    }

    private async Task CheckLimitsAsync(Account from, decimal amount, DateTimeOffset now, CancellationToken ct)
    {
        if (amount > MaxSingleTransfer)
            throw new DomainException("limit_single", $"Один перевод — не больше {MaxSingleTransfer:N0} ₽");

        var msk = now.ToOffset(Moscow);
        // Npgsql принимает для timestamptz только UTC, поэтому переводим начало суток обратно в UTC.
        var dayStart = new DateTimeOffset(msk.Date, Moscow).ToUniversalTime();

        var sentToday = await db.Transfers
            .Where(t => t.FromAccountId == from.Id && t.Status == TransferStatus.Completed && t.CreatedAt >= dayStart)
            .Join(db.Accounts, t => t.ToAccountId, a => a.Id, (t, a) => new { t.Amount, a.ClientId })
            .Where(x => x.ClientId != from.ClientId)
            .SumAsync(x => x.Amount, ct);

        if (sentToday + amount > DailyLimitToOthers)
            throw new DomainException("limit_daily",
                $"Суточный лимит переводов {DailyLimitToOthers:N0} ₽. Сегодня уже отправлено {sentToday:N2} ₽");
    }

    private async Task<TransferOutcome?> FindExistingAsync(TransferCommand cmd, CancellationToken ct)
    {
        var t = await db.Transfers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == cmd.IdempotencyKey, ct);
        if (t is null) return null;

        // Тот же ключ, но другие параметры — это ошибка клиента, а не повтор.
        if (t.FromAccountId != cmd.FromAccountId || t.ToAccountId != cmd.ToAccountId || t.Amount != cmd.Amount)
            throw new DomainException("idempotency_conflict", "Ключ идемпотентности уже использован для другого перевода");

        var fromAccount = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == t.FromAccountId)
            .Select(a => new { a.ClientId })
            .SingleAsync(ct);
        if (cmd.InitiatorClientId is { } owner && fromAccount.ClientId != owner)
            throw new DomainException("account_not_found", "Счёт списания не найден");

        var balanceAfter = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.TransferId == t.Id && e.AccountId == t.FromAccountId)
            .Select(e => e.BalanceAfter)
            .SingleAsync(ct);

        return new TransferOutcome(t, Created: false, balanceAfter);
    }
}
