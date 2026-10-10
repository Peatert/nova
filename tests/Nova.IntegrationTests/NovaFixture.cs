using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Nova.Domain;
using Nova.Domain.Entities;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;
using Nova.Infrastructure.Transfers;
using Testcontainers.PostgreSql;

namespace Nova.IntegrationTests;

/// <summary>
/// Поднимает настоящий PostgreSQL 17 в Docker и API поверх него. Один раз на все тесты.
/// Никаких моков БД: блокировки строк, партиции, уникальные индексы проверяются по-настоящему.
/// Требуется запущенный Docker Desktop.
/// </summary>
public sealed class NovaFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("nova_test")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString => _db.GetConnectionString();

    /// <summary>Технический счёт «банка»: через него в тестах зачисляются деньги клиентам.</summary>
    public Guid BankAccountId { get; private set; }

    private static int _phoneCounter;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        // Строку подключения передаём через переменную окружения: Program читает конфигурацию
        // ещё до того, как WebApplicationFactory успевает применить свои настройки.
        // Пул меньше лимита соединений PostgreSQL (100): тест на 100 параллельных переводов
        // должен упираться в блокировки строк, а не в «too many clients».
        Environment.SetEnvironmentVariable("ConnectionStrings__Nova", _db.GetConnectionString() + ";Maximum Pool Size=50");
        Factory = new WebApplicationFactory<Program>();
        _ = Factory.Server; // старт API: в Development он сам применяет все миграции

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
        var bank = new Client { FullName = "АО «Nova» — технические счета", Phone = "+70000000000", PasswordHash = "!" };
        var corr = new Account
        {
            ClientId = bank.Id, Number = "30102810000000000001",
            Type = AccountType.Technical, CreditLimit = 1_000_000_000m,
        };
        db.AddRange(bank, corr);
        await db.SaveChangesAsync();
        BankAccountId = corr.Id;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    public static string NewPhone() => $"+7999{Interlocked.Increment(ref _phoneCounter):D7}";

    /// <summary>Клиент с дебетовым счётом и стартовым балансом (зачислен честным переводом с техсчёта).</summary>
    public async Task<(Guid ClientId, Guid AccountId, string Phone)> CreateClientAsync(decimal balance, string password = "TestPassword123")
    {
        var phone = NewPhone();
        Guid clientId, accountId;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
            var client = new Client { FullName = "Тестов Тест Тестович", Phone = phone, PasswordHash = PasswordHasher.Hash(password) };
            var account = new Account
            {
                ClientId = client.Id,
                Number = "40817810" + Random.Shared.NextInt64(0, 999_999_999_999).ToString("D12"),
            };
            db.AddRange(client, account);
            await db.SaveChangesAsync();
            (clientId, accountId) = (client.Id, account.Id);
        }
        if (balance > 0) await TransferAsync(BankAccountId, accountId, balance);
        return (clientId, accountId, phone);
    }

    public async Task<TransferOutcome> TransferAsync(Guid from, Guid to, decimal amount, Guid? key = null, Guid? initiator = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TransferService>();
        return await service.ExecuteAsync(new TransferCommand(from, to, amount, null, key ?? Guid.NewGuid(), initiator));
    }

    public async Task<decimal> BalanceAsync(Guid accountId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT balance FROM accounts.accounts WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", accountId);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Сверка всей книги: (счетов с расхождением, дебет, кредит).</summary>
    public async Task<(long Mismatched, decimal Debits, decimal Credits)> ReconcileAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        long mismatched;
        await using (var cmd = new NpgsqlCommand(Sql.ReconcileAccounts, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            mismatched = r.GetInt64(1);
        }
        await using (var cmd = new NpgsqlCommand(Sql.ReconcileLedger, conn))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            return (mismatched, r.GetDecimal(0), r.GetDecimal(1));
        }
    }

    public async Task<long> CountAsync(string sql, Guid id)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}

[CollectionDefinition(Name)]
public sealed class NovaCollection : ICollectionFixture<NovaFixture>
{
    public const string Name = "nova";
}
