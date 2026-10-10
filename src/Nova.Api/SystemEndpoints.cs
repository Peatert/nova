using Microsoft.EntityFrameworkCore;
using Nova.Domain.Entities;
using Nova.Domain.Security;
using Nova.Infrastructure.Persistence;

namespace Nova.Api;

/// <summary>Служебные эндпоинты. Каждый закрыт конкретным правом.</summary>
public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var system = app.MapGroup("/api/system").WithTags("System");

        // Сводка по объёму данных.
        system.MapGet("/stats", async (NovaDbContext db, CancellationToken ct) => new
        {
            Clients = await db.Clients.LongCountAsync(ct),
            Accounts = await db.Accounts.LongCountAsync(ct),
            Cards = await db.Cards.LongCountAsync(ct),
            Transfers = await db.Transfers.LongCountAsync(ct),
            LedgerEntries = await db.LedgerEntries.LongCountAsync(ct),
            Staff = await db.StaffMembers.LongCountAsync(ct),
        }).RequireAuthorization(Permissions.ReportsRead);

        // Матрица ролей и прав.
        system.MapGet("/roles", async (NovaDbContext db, CancellationToken ct) =>
            await db.Roles.AsNoTracking().OrderBy(r => r.Code)
                .Select(r => new { r.Code, r.Name, r.Permissions })
                .ToListAsync(ct))
            .RequireAuthorization(Permissions.StaffManage);

        // Целостность аудит-лога по цепочке хешей.
        system.MapGet("/audit/verify", async (NovaDbContext db, CancellationToken ct) =>
        {
            var entries = await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
            var broken = AuditLog.FindBrokenLink(entries);
            return new { Total = entries.Count, Intact = broken is null, FirstBrokenId = broken };
        }).RequireAuthorization(Permissions.AuditRead);

        // Последние записи аудита — видно входы, ошибки, блокировки.
        system.MapGet("/audit/recent", async (NovaDbContext db, int? take, CancellationToken ct) =>
            await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.Id).Take(Math.Clamp(take ?? 50, 1, 500))
                .Select(a => new { a.Id, a.CreatedAt, a.ActorType, a.ActorId, a.Action, a.Target, a.PayloadJson, a.Ip })
                .ToListAsync(ct))
            .RequireAuthorization(Permissions.AuditRead);

        // Сверка: баланс каждого счёта = сумма его проводок, сумма дебетов = сумма кредитов.
        system.MapGet("/reconcile", async (NovaDbContext db, CancellationToken ct) =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                long accounts, mismatched;
                decimal debits, credits;

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = Sql.ReconcileAccounts;
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    await r.ReadAsync(ct);
                    accounts = r.GetInt64(0);
                    mismatched = r.GetInt64(1);
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = Sql.ReconcileLedger;
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    await r.ReadAsync(ct);
                    debits = r.GetDecimal(0);
                    credits = r.GetDecimal(1);
                }

                return Results.Ok(new
                {
                    Ok = mismatched == 0 && debits == credits,
                    Accounts = accounts,
                    MismatchedAccounts = mismatched,
                    TotalDebits = debits,
                    TotalCredits = credits,
                });
            }
            finally
            {
                await conn.CloseAsync();
            }
        }).RequireAuthorization(Permissions.ReportsRead);

        // Партиции книги проводок.
        system.MapGet("/partitions", async (NovaDbContext db, CancellationToken ct) =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = Sql.LedgerPartitions;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var list = new List<object>();
                while (await r.ReadAsync(ct))
                {
                    var rows = r.GetInt64(1);
                    if (rows == 0) continue;
                    list.Add(new { Name = r.GetString(0), Rows = rows, SizeMb = Math.Round(r.GetInt64(2) / 1048576.0, 1) });
                }
                return list;
            }
            finally
            {
                await conn.CloseAsync();
            }
        }).RequireAuthorization(Permissions.ReportsRead);
    }
}
