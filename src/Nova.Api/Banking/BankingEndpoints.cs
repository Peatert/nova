using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nova.Api.Auth;
using Nova.Domain;
using Nova.Domain.Clients;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Transfers;

namespace Nova.Api.Banking;

public sealed record TransferRequest(Guid FromAccountId, string? ToPhone, Guid? ToAccountId, decimal Amount, string? Comment);

/// <summary>Счета, выписка и переводы клиента. Всё — только по своим счетам.</summary>
public static class BankingEndpoints
{
    private static readonly TimeSpan DefaultStatementPeriod = TimeSpan.FromDays(90);
    private static readonly TimeSpan MaxStatementPeriod = TimeSpan.FromDays(366);

    public static void MapBankingEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization("client");
        accounts.MapGet("", GetAccounts);
        accounts.MapGet("/{id:guid}/statement", GetStatement);

        var transfers = app.MapGroup("/api/transfers").WithTags("Transfers").RequireAuthorization("client");
        transfers.MapGet("/recipient", FindRecipient);
        transfers.MapPost("", CreateTransfer);
        transfers.MapGet("/{id:guid}", GetTransfer);
    }

    // ───────────────────────── Счета ─────────────────────────

    private static async Task<IResult> GetAccounts(ClaimsPrincipal user, NovaDbContext db, CancellationToken ct)
    {
        var clientId = ClientId(user);
        var list = await db.Accounts.AsNoTracking()
            .Where(a => a.ClientId == clientId && a.Status != AccountStatus.Closed)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Number,
                a.Type,
                a.Currency,
                a.Balance,
                a.CreditLimit,
                Available = a.Balance + a.CreditLimit,
                a.Status,
                Cards = a.Cards.OrderBy(c => c.CreatedAt).Select(c => new { c.Id, Last4 = c.PanLast4, c.ExpiresAt, c.Status }),
            })
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    /// <summary>
    /// Выписка всегда за период (по умолчанию 90 дней, максимум год).
    /// Это вывод из замеров недели 2: без периода планировщик перебирает все партиции,
    /// с периодом — читает только нужные месяцы.
    /// Пагинация курсором: next — время последней строки, передаётся в before следующего запроса.
    /// </summary>
    private static async Task<IResult> GetStatement(
        Guid id,
        [FromQuery(Name = "from")] DateTimeOffset? periodStart,
        [FromQuery(Name = "to")] DateTimeOffset? periodEnd,
        DateTimeOffset? before, int? limit,
        ClaimsPrincipal user, NovaDbContext db, TimeProvider time, CancellationToken ct)
    {
        var clientId = ClientId(user);
        if (!await db.Accounts.AnyAsync(a => a.Id == id && a.ClientId == clientId, ct))
            return AuthEndpoints.Problem(404, "Счёт не найден");

        // Npgsql пишет в timestamptz только UTC.
        var periodTo = (periodEnd ?? time.GetUtcNow()).ToUniversalTime();
        var periodFrom = (periodStart ?? periodTo - DefaultStatementPeriod).ToUniversalTime();
        if (periodFrom >= periodTo)
            return AuthEndpoints.Problem(400, "Начало периода должно быть раньше конца");
        if (periodTo - periodFrom > MaxStatementPeriod)
            return AuthEndpoints.Problem(400, "Период выписки — не больше года");
        var cursor = before?.ToUniversalTime();
        var take = Math.Clamp(limit ?? 50, 1, 200);

        var rows = await (
                from e in db.LedgerEntries.AsNoTracking()
                join t in db.Transfers.AsNoTracking() on e.TransferId equals t.Id
                where e.AccountId == id && e.CreatedAt >= periodFrom && e.CreatedAt < periodTo
                      && (cursor == null || e.CreatedAt < cursor)
                orderby e.CreatedAt descending
                select new
                {
                    e.TransferId,
                    e.CreatedAt,
                    e.Direction,
                    e.Amount,
                    e.BalanceAfter,
                    t.Comment,
                    CounterpartyAccountId = e.Direction == EntryDirection.Debit ? t.ToAccountId : t.FromAccountId,
                })
            .Take(take)
            .ToListAsync(ct);

        // Имена контрагентов одним запросом. Технический корсчёт (покупки, зарплаты) имени не показывает.
        var counterpartyIds = rows.Select(r => r.CounterpartyAccountId).Distinct().ToList();
        var names = await db.Accounts.AsNoTracking()
            .Where(a => counterpartyIds.Contains(a.Id) && a.Type != AccountType.Technical)
            .Select(a => new { a.Id, a.ClientId, a.Client!.FullName })
            .ToDictionaryAsync(a => a.Id, ct);

        var items = rows.Select(r =>
        {
            names.TryGetValue(r.CounterpartyAccountId, out var cp);
            return new
            {
                r.TransferId,
                r.CreatedAt,
                r.Direction,
                Amount = r.Direction == EntryDirection.Debit ? -r.Amount : r.Amount,
                r.BalanceAfter,
                Description = r.Comment ?? "Перевод",
                Counterparty = cp is null ? null : cp.ClientId == clientId ? "Между своими счетами" : DisplayName(cp.FullName),
            };
        }).ToList();

        return Results.Ok(new
        {
            AccountId = id,
            From = periodFrom,
            To = periodTo,
            Items = items,
            Next = items.Count == take ? items[^1].CreatedAt : (DateTimeOffset?)null,
        });
    }

    // ───────────────────────── Переводы ─────────────────────────

    /// <summary>Как в мобильных банках: ввёл телефон — увидел «Алексей К.» перед отправкой.</summary>
    private static async Task<IResult> FindRecipient(string? phone, ClaimsPrincipal user, NovaDbContext db, CancellationToken ct)
    {
        if (!PhoneNumber.TryNormalize(phone, out var normalized))
            return AuthEndpoints.Problem(400, "Некорректный номер телефона");
        var client = await db.Clients.AsNoTracking()
            .Where(c => c.Phone == normalized)
            .Select(c => new { c.Id, c.FullName })
            .SingleOrDefaultAsync(ct);
        if (client is null)
            return AuthEndpoints.Problem(404, "Клиент Nova с таким номером не найден");
        if (client.Id == ClientId(user))
            return AuthEndpoints.Problem(400, "Это ваш номер. Для перевода между своими счетами выберите счёт");
        return Results.Ok(new { Name = DisplayName(client.FullName), Bank = "Nova" });
    }

    private static async Task<IResult> CreateTransfer(
        TransferRequest req, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal user, NovaDbContext db, TransferService transfers, CancellationToken ct)
    {
        if (!Guid.TryParse(idempotencyKey, out var key))
            return AuthEndpoints.Problem(400, "Нужен заголовок Idempotency-Key (GUID)",
                "Клиент генерирует новый GUID на каждый перевод и повторяет его при повторной отправке.");
        if ((req.ToPhone is null) == (req.ToAccountId is null))
            return AuthEndpoints.Problem(400, "Укажите получателя: либо toPhone, либо toAccountId (свой счёт)");

        var clientId = ClientId(user);
        Guid toAccountId;
        string recipient;

        if (req.ToPhone is not null)
        {
            if (!PhoneNumber.TryNormalize(req.ToPhone, out var phone))
                return AuthEndpoints.Problem(400, "Некорректный номер телефона");
            var target = await db.Clients.AsNoTracking()
                .Where(c => c.Phone == phone)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    AccountId = c.Accounts
                        .Where(a => a.Type == AccountType.Debit && a.Status == AccountStatus.Active)
                        .OrderBy(a => a.CreatedAt)
                        .Select(a => (Guid?)a.Id)
                        .FirstOrDefault(),
                })
                .SingleOrDefaultAsync(ct);
            if (target?.AccountId is null)
                return AuthEndpoints.Problem(404, "Клиент Nova с таким номером не найден");
            if (target.Id == clientId)
                return AuthEndpoints.Problem(400, "Это ваш номер. Для перевода между своими счетами укажите toAccountId");
            toAccountId = target.AccountId.Value;
            recipient = DisplayName(target.FullName);
        }
        else
        {
            // По id — только на свои счета. Перевод на чужой счёт по id запрещён: id не должны быть «реквизитами».
            var own = await db.Accounts.AnyAsync(a => a.Id == req.ToAccountId && a.ClientId == clientId, ct);
            if (!own) return AuthEndpoints.Problem(404, "Счёт получателя не найден");
            toAccountId = req.ToAccountId!.Value;
            recipient = "Между своими счетами";
        }

        try
        {
            var outcome = await transfers.ExecuteAsync(
                new TransferCommand(req.FromAccountId, toAccountId, req.Amount, req.Comment, key, clientId), ct);
            var t = outcome.Transfer;
            var body = new
            {
                t.Id,
                t.Status,
                t.Amount,
                t.Currency,
                t.Comment,
                t.CreatedAt,
                Recipient = recipient,
                BalanceAfter = outcome.FromBalanceAfter,
            };
            // Первый раз — 201 Created. Повтор с тем же ключом — 200 и тот же перевод.
            return outcome.Created ? Results.Created($"/api/transfers/{t.Id}", body) : Results.Ok(body);
        }
        catch (DomainException ex)
        {
            return ex.Code switch
            {
                "account_not_found" or "recipient_not_found" => AuthEndpoints.Problem(404, ex.Message),
                "idempotency_conflict" => AuthEndpoints.Problem(409, ex.Message),
                "insufficient_funds" or "limit_single" or "limit_daily" or "account_inactive" or "account_closed"
                    or "currency_mismatch" => AuthEndpoints.Problem(422, ex.Message),
                _ => AuthEndpoints.Problem(400, ex.Message),
            };
        }
    }

    private static async Task<IResult> GetTransfer(Guid id, ClaimsPrincipal user, NovaDbContext db, CancellationToken ct)
    {
        var clientId = ClientId(user);
        var t = await db.Transfers.AsNoTracking()
            .Where(x => x.Id == id)
            .Where(x => db.Accounts.Any(a => (a.Id == x.FromAccountId || a.Id == x.ToAccountId) && a.ClientId == clientId))
            .Select(x => new { x.Id, x.Status, x.Amount, x.Currency, x.Comment, x.CreatedAt, x.FromAccountId, x.ToAccountId })
            .SingleOrDefaultAsync(ct);
        return t is null ? AuthEndpoints.Problem(404, "Перевод не найден") : Results.Ok(t);
    }

    // ───────────────────────── Вспомогательное ─────────────────────────

    private static Guid ClientId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(NovaClaims.Subject)!);

    /// <summary>«Смирнов Алексей Петрович» → «Алексей С.» — ровно столько, сколько показывают банки.</summary>
    internal static string DisplayName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[1]} {parts[0][0]}." : parts.FirstOrDefault() ?? "Клиент Nova";
    }
}
