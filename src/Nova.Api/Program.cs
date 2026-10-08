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

app.Run();
