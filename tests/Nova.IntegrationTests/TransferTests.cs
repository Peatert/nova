using Nova.Domain;

namespace Nova.IntegrationTests;

[Collection(NovaCollection.Name)]
public class TransferTests(NovaFixture f)
{
    private const string EntriesOfTransfer = "SELECT count(*) FROM transactions.ledger_entries WHERE transfer_id = @id";

    [Fact]
    public async Task Transfer_moves_money_and_writes_two_entries()
    {
        var a = await f.CreateClientAsync(1_000m);
        var b = await f.CreateClientAsync(0m);

        var outcome = await f.TransferAsync(a.AccountId, b.AccountId, 250.50m, initiator: a.ClientId);

        Assert.True(outcome.Created);
        Assert.Equal(749.50m, await f.BalanceAsync(a.AccountId));
        Assert.Equal(250.50m, await f.BalanceAsync(b.AccountId));
        Assert.Equal(749.50m, outcome.FromBalanceAfter);
        Assert.Equal(2, await f.CountAsync(EntriesOfTransfer, outcome.Transfer.Id));
    }

    [Fact]
    public async Task Insufficient_funds_changes_nothing()
    {
        var a = await f.CreateClientAsync(100m);
        var b = await f.CreateClientAsync(0m);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            f.TransferAsync(a.AccountId, b.AccountId, 100.01m, initiator: a.ClientId));

        Assert.Equal("insufficient_funds", ex.Code);
        Assert.Equal(100m, await f.BalanceAsync(a.AccountId));
        Assert.Equal(0m, await f.BalanceAsync(b.AccountId));
    }

    [Fact]
    public async Task Cannot_spend_from_someone_elses_account()
    {
        var victim = await f.CreateClientAsync(1_000m);
        var thief = await f.CreateClientAsync(0m);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            f.TransferAsync(victim.AccountId, thief.AccountId, 500m, initiator: thief.ClientId));

        Assert.Equal("account_not_found", ex.Code);
        Assert.Equal(1_000m, await f.BalanceAsync(victim.AccountId));
    }

    [Fact]
    public async Task Retry_with_same_key_does_not_charge_twice()
    {
        var a = await f.CreateClientAsync(1_000m);
        var b = await f.CreateClientAsync(0m);
        var key = Guid.NewGuid();

        var first = await f.TransferAsync(a.AccountId, b.AccountId, 300m, key, a.ClientId);
        var second = await f.TransferAsync(a.AccountId, b.AccountId, 300m, key, a.ClientId);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Transfer.Id, second.Transfer.Id);
        Assert.Equal(700m, await f.BalanceAsync(a.AccountId));
    }

    [Fact]
    public async Task Same_key_with_different_amount_is_rejected()
    {
        var a = await f.CreateClientAsync(1_000m);
        var b = await f.CreateClientAsync(0m);
        var key = Guid.NewGuid();

        await f.TransferAsync(a.AccountId, b.AccountId, 100m, key, a.ClientId);
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            f.TransferAsync(a.AccountId, b.AccountId, 200m, key, a.ClientId));

        Assert.Equal("idempotency_conflict", ex.Code);
        Assert.Equal(900m, await f.BalanceAsync(a.AccountId));
    }

    [Fact]
    public async Task Same_key_sent_concurrently_creates_one_transfer()
    {
        var a = await f.CreateClientAsync(1_000m);
        var b = await f.CreateClientAsync(0m);
        var key = Guid.NewGuid();

        // 10 одинаковых запросов одновременно — как если бы приложение 10 раз нажало «повторить».
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => f.TransferAsync(a.AccountId, b.AccountId, 100m, key, a.ClientId))));

        Assert.Single(results, r => r.Created);
        Assert.Single(results.Select(r => r.Transfer.Id).Distinct());
        Assert.Equal(900m, await f.BalanceAsync(a.AccountId));
    }

    [Fact]
    public async Task Hundred_parallel_transfers_never_overdraw()
    {
        // На счёте 5 000 ₽, одновременно летят 100 переводов по 100 ₽.
        // Ровно 50 должны пройти, 50 — получить «недостаточно средств». Баланс — ровно 0, не минус.
        var a = await f.CreateClientAsync(5_000m);
        var b = await f.CreateClientAsync(0m);

        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(async () =>
        {
            try
            {
                await f.TransferAsync(a.AccountId, b.AccountId, 100m, initiator: a.ClientId);
                return true;
            }
            catch (DomainException e) when (e.Code == "insufficient_funds")
            {
                return false;
            }
        }));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(50, results.Count(ok => ok));
        Assert.Equal(0m, await f.BalanceAsync(a.AccountId));
        Assert.Equal(5_000m, await f.BalanceAsync(b.AccountId));

        var (mismatched, debits, credits) = await f.ReconcileAsync();
        Assert.Equal(0, mismatched);
        Assert.Equal(debits, credits);
    }

    [Fact]
    public async Task Opposite_transfers_do_not_deadlock()
    {
        // A→B и B→A одновременно. Без упорядоченных блокировок это классический дедлок.
        var a = await f.CreateClientAsync(10_000m);
        var b = await f.CreateClientAsync(10_000m);

        var tasks = Enumerable.Range(0, 60).Select(i => Task.Run(() =>
            i % 2 == 0
                ? f.TransferAsync(a.AccountId, b.AccountId, 10m, initiator: a.ClientId)
                : f.TransferAsync(b.AccountId, a.AccountId, 10m, initiator: b.ClientId)));

        var all = Task.WhenAll(tasks);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(all, finished);
        await all; // пробрасывает исключения, если были

        Assert.Equal(10_000m, await f.BalanceAsync(a.AccountId));
        Assert.Equal(10_000m, await f.BalanceAsync(b.AccountId));
    }

    [Fact]
    public async Task Single_transfer_limit_applies_to_other_clients_only()
    {
        var a = await f.CreateClientAsync(400_000m);
        var b = await f.CreateClientAsync(0m);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            f.TransferAsync(a.AccountId, b.AccountId, 300_000.01m, initiator: a.ClientId));
        Assert.Equal("limit_single", ex.Code);
        Assert.Equal(400_000m, await f.BalanceAsync(a.AccountId));
    }
}
