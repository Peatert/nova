using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Domain;
using Nova.Domain.Entities;
using Nova.Infrastructure.Persistence;

namespace Nova.Infrastructure.Audit;

/// <summary>
/// Добавляет запись в аудит-лог с цепочкой хешей.
///
/// Чтобы два параллельных запроса не прочитали один и тот же «последний хеш» и не разорвали цепочку,
/// запись идёт под транзакционной advisory-блокировкой PostgreSQL: пишет строго один поток за раз.
///
/// ВАЖНО: метод вызывает SaveChanges, поэтому сохраняет и все остальные изменения контекста
/// (счётчик неудачных входов, новый refresh-токен и т.д.) — в одной транзакции с записью аудита.
/// Действие и запись о нём либо сохраняются вместе, либо не сохраняются вовсе.
/// </summary>
public sealed class AuditWriter(NovaDbContext db, TimeProvider time)
{

    public async Task WriteAsync(ActorType actorType, Guid? actorId, string action,
                                 string? target = null, object? payload = null, string? ip = null,
                                 CancellationToken ct = default)
    {
        var ownTx = db.Database.CurrentTransaction is null;
        await using var tx = ownTx ? await db.Database.BeginTransactionAsync(ct) : null;

        // 7340101 — произвольная константа: номер «замка» цепочки аудита.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7340101)", ct);

        var prev = await db.AuditLogs
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(ct) ?? AuditLog.GenesisHash;

        var entry = new AuditLog
        {
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            Target = target,
            PayloadJson = payload is null ? null : JsonSerializer.Serialize(payload),
            Ip = ip,
            PrevHash = prev,
            CreatedAt = time.GetUtcNow(),
        };
        entry.Seal();
        db.AuditLogs.Add(entry);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }
}
