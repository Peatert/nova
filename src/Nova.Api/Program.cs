using Microsoft.EntityFrameworkCore;
using Nova.Domain.Entities;
using Nova.Infrastructure;
using Nova.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNovaInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<NovaDbContext>("postgres");

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json

    // В разработке накатываем миграции при старте. В проде — отдельным шагом деплоя.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

var system = app.MapGroup("/api/system").WithTags("System");

// Сводка по объёму данных — проверка, что БД живая и наполнена.
system.MapGet("/stats", async (NovaDbContext db, CancellationToken ct) => new
{
    Clients = await db.Clients.LongCountAsync(ct),
    Accounts = await db.Accounts.LongCountAsync(ct),
    Cards = await db.Cards.LongCountAsync(ct),
    Transfers = await db.Transfers.LongCountAsync(ct),
    LedgerEntries = await db.LedgerEntries.LongCountAsync(ct),
    Staff = await db.StaffMembers.LongCountAsync(ct),
});

// Матрица ролей и прав.
system.MapGet("/roles", async (NovaDbContext db, CancellationToken ct) =>
    await db.Roles.AsNoTracking().OrderBy(r => r.Code)
        .Select(r => new { r.Code, r.Name, r.Permissions })
        .ToListAsync(ct));

// Проверка целостности аудит-лога по цепочке хешей.
// TODO (неделя 2): закрыть правом audit.read после появления авторизации.
system.MapGet("/audit/verify", async (NovaDbContext db, CancellationToken ct) =>
{
    var entries = await db.AuditLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct);
    var broken = AuditLog.FindBrokenLink(entries);
    return new { Total = entries.Count, Intact = broken is null, FirstBrokenId = broken };
});

// Сверка: баланс каждого счёта = сумма его проводок, сумма дебетов = сумма кредитов.
// TODO (неделя 3): закрыть правом reports.read.
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
});

// Партиции книги проводок: видно, как 5 млн строк разложены по месяцам.
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
            if (rows == 0) continue; // пустые будущие месяцы не показываем
            list.Add(new { Name = r.GetString(0), Rows = rows, SizeMb = Math.Round(r.GetInt64(2) / 1048576.0, 1) });
        }
        return list;
    }
    finally
    {
        await conn.CloseAsync();
    }
});

app.Run();
