using System.Diagnostics;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Nova.Domain;
using Nova.Domain.Cards;
using Nova.Domain.Entities;
using Nova.Domain.Security;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;

namespace Nova.Seeder;

/// <summary>
/// Генерирует связную историю банка: клиенты → счета → карты → переводы с двойной записью.
///
/// Как устроено:
///  • Балансы всех счетов держим в памяти (массивы на ~350 тыс. элементов).
///  • Переводы генерируются строго по времени, поэтому balance_after в каждой проводке точный,
///    а ни один дебетовый счёт не уходит в минус: перевод создаётся, только если хватает денег.
///  • Деньги приходят в банк и уходят из него через технический корсчёт (зарплаты, покупки).
///  • Вставка — через бинарный COPY пачками по 50 тыс. переводов. Это в 50–100 раз быстрее INSERT через EF.
///  • В конце балансы пишутся одним UPDATE, и выполняется сверка: баланс = сумма проводок.
/// </summary>
internal sealed class DataGenerator(SeedOptions o, NpgsqlConnection conn, FieldEncryptor enc)
{
    public const string ClientPassword = "Nova-client-2026!";
    public const string StaffPassword = "Nova-staff-2026!";

    private const int ChunkSize = 50_000;
    private const int Corr = 0; // индекс корсчёта в массивах

    private readonly Random _rng = new(o.Seed);
    private readonly DateTime _end = DateTime.UtcNow;
    private DateTime _start;

    // Состояние счетов: индекс в массиве = внутренний номер счёта.
    private int _accCount;
    private Guid[] _accId = [];
    private Guid[] _accClient = [];
    private AccountType[] _accType = [];
    private decimal[] _limit = [];
    private decimal[] _bal = [];
    private DateTime[] _accCreated = [];

    // Счета клиента по его индексу (-1 — счёта такого типа нет).
    private int[] _debitOf = [], _savingsOf = [], _creditOf = [];

    private readonly record struct Tx(Guid Id, int From, int To, decimal Amount, string Comment,
                                      DateTime At, decimal FromAfter, decimal ToAfter);

    public void Run()
    {
        _start = _end.AddMonths(-o.Months);
        var total = Stopwatch.StartNew();
        Console.WriteLine($"История: {_start:yyyy-MM-dd} — {_end:yyyy-MM-dd} UTC, зерно {o.Seed}");

        Step("Клиенты и счета (в памяти) + COPY клиентов", SeedClients);
        Step("COPY счетов", CopyAccounts);
        Step("COPY карт", CopyCards);
        Step("Переводы и проводки", SeedTransfers);
        Step("Запись итоговых балансов", WriteBalances);
        Step("Сотрудники (по одному на роль)", SeedStaff);
        Step("ANALYZE (статистика для планировщика)", () => Exec("ANALYZE;"));
        Step("Запись в аудит-лог", WriteAudit);
        Step("Сверка", Reconcile);

        Console.WriteLine();
        Console.WriteLine($"Готово за {total.Elapsed:mm\\:ss}.");
        Console.WriteLine($"Пароль любого клиента:     {ClientPassword}  (логин — телефон, например +79990000000)");
        Console.WriteLine($"Пароль любого сотрудника:  {StaffPassword}  (логины: owner, operator, support, credit, risk, accountant, auditor, sysadmin)");
    }

    // ───────────────────────── Клиенты и счета ─────────────────────────

    private void SeedClients()
    {
        var n = o.Clients;
        var cap = 1 + n * 3;
        _accId = new Guid[cap];
        _accClient = new Guid[cap];
        _accType = new AccountType[cap];
        _limit = new decimal[cap];
        _bal = new decimal[cap];
        _accCreated = new DateTime[cap];
        _debitOf = new int[n];
        _savingsOf = new int[n];
        _creditOf = new int[n];

        // Один хеш на всех тестовых клиентов: PBKDF2 на 200 тыс. паролей занял бы ~20 минут.
        var clientHash = PasswordHasher.Hash(ClientPassword);

        using var imp = conn.BeginBinaryImport(
            "COPY clients.clients (id, full_name, phone, password_hash, passport_enc, totp_secret_enc, created_at) FROM STDIN (FORMAT BINARY)");

        // Технический клиент — владелец корсчёта. Вход запрещён ("!" не является валидным хешем).
        var bankCreated = _start.AddYears(-3);
        var bankId = Guid.CreateVersion7(bankCreated);
        WriteClient(imp, bankId, "АО «Nova» — технические счета", "+70000000000", "!", null, bankCreated);
        var corr = AddAccount(bankId, AccountType.Technical, 1_000_000_000_000m, bankCreated);
        Debug.Assert(corr == Corr);

        for (var i = 0; i < n; i++)
        {
            var male = _rng.Next(2) == 0;
            var surname = Pick(Names.Surnames);
            var (pm, pf) = Pick(Names.Patronymics);
            var fullName = male
                ? $"{surname} {Pick(Names.MaleFirst)} {pm}"
                : $"{Names.FemaleSurname(surname)} {Pick(Names.FemaleFirst)} {pf}";

            var created = _start.AddDays(-_rng.NextDouble() * 730);
            var id = Guid.CreateVersion7(created);
            var phone = $"+7999{i:D7}";
            var passport = $"{_rng.Next(1000, 10000)} {_rng.Next(100000, 1000000)}";

            WriteClient(imp, id, fullName, phone, clientHash, enc.Encrypt(passport), created);

            _debitOf[i] = AddAccount(id, AccountType.Debit, 0m, created);
            _savingsOf[i] = _rng.NextDouble() < 0.5
                ? AddAccount(id, AccountType.Savings, 0m, created.AddDays(_rng.Next(0, 60)))
                : -1;
            _creditOf[i] = _rng.NextDouble() < 0.25
                ? AddAccount(id, AccountType.Credit, _rng.Next(3, 31) * 10_000m, created.AddDays(_rng.Next(0, 90)))
                : -1;
        }
        imp.Complete();
        Console.Write($"клиентов: {n + 1:N0}, счетов: {_accCount:N0}. ");
    }

    private static void WriteClient(NpgsqlBinaryImporter imp, Guid id, string name, string phone,
                                    string hash, string? passportEnc, DateTime created)
    {
        imp.StartRow();
        imp.Write(id, NpgsqlDbType.Uuid);
        imp.Write(name, NpgsqlDbType.Text);
        imp.Write(phone, NpgsqlDbType.Text);
        imp.Write(hash, NpgsqlDbType.Text);
        if (passportEnc is null) imp.WriteNull(); else imp.Write(passportEnc, NpgsqlDbType.Text);
        imp.WriteNull(); // totp_secret_enc — 2FA включается пользователем на неделе 3
        imp.Write(created, NpgsqlDbType.TimestampTz);
    }

    private int AddAccount(Guid clientId, AccountType type, decimal limit, DateTime created)
    {
        var i = _accCount++;
        _accId[i] = Guid.CreateVersion7(created);
        _accClient[i] = clientId;
        _accType[i] = type;
        _limit[i] = limit;
        _accCreated[i] = created;
        return i;
    }

    /// <summary>20-значный номер: балансовый счёт (5) + валюта 810 + резерв + порядковый номер.</summary>
    private static string AccountNumber(AccountType type, int index) => type switch
    {
        AccountType.Debit => "40817810",
        AccountType.Savings => "42301810",
        AccountType.Credit => "45507810",
        _ => "30102810",
    } + index.ToString("D12");

    private void CopyAccounts()
    {
        using var imp = conn.BeginBinaryImport(
            "COPY accounts.accounts (id, client_id, number, type, currency, balance, credit_limit, status, created_at) FROM STDIN (FORMAT BINARY)");
        for (var i = 0; i < _accCount; i++)
        {
            imp.StartRow();
            imp.Write(_accId[i], NpgsqlDbType.Uuid);
            imp.Write(_accClient[i], NpgsqlDbType.Uuid);
            imp.Write(AccountNumber(_accType[i], i), NpgsqlDbType.Text);
            imp.Write(_accType[i].ToString(), NpgsqlDbType.Text);
            imp.Write("RUB", NpgsqlDbType.Text);
            imp.Write(0m, NpgsqlDbType.Numeric); // реальные балансы запишем после генерации переводов
            imp.Write(_limit[i], NpgsqlDbType.Numeric);
            imp.Write(nameof(AccountStatus.Active), NpgsqlDbType.Text);
            imp.Write(_accCreated[i], NpgsqlDbType.TimestampTz);
        }
        imp.Complete();
    }

    private void CopyCards()
    {
        var today = DateOnly.FromDateTime(_end);
        var count = 0;
        using var imp = conn.BeginBinaryImport(
            "COPY accounts.cards (id, account_id, pan_enc, pan_last4, expires_at, status, created_at) FROM STDIN (FORMAT BINARY)");
        for (var i = 0; i < _accCount; i++)
        {
            if (_accType[i] is not (AccountType.Debit or AccountType.Credit)) continue;

            var pan = NewPan();
            var exp = _accCreated[i].AddYears(4);
            var expires = new DateOnly(exp.Year, exp.Month, 1).AddMonths(1).AddDays(-1); // последний день месяца
            var status = expires < today ? CardStatus.Expired
                       : _rng.NextDouble() < 0.02 ? CardStatus.Blocked
                       : CardStatus.Active;

            imp.StartRow();
            imp.Write(Guid.CreateVersion7(_accCreated[i]), NpgsqlDbType.Uuid);
            imp.Write(_accId[i], NpgsqlDbType.Uuid);
            imp.Write(enc.Encrypt(pan), NpgsqlDbType.Text);
            imp.Write(pan[^4..], NpgsqlDbType.Text);
            imp.Write(expires, NpgsqlDbType.Date);
            imp.Write(status.ToString(), NpgsqlDbType.Text);
            imp.Write(_accCreated[i], NpgsqlDbType.TimestampTz);
            count++;
        }
        imp.Complete();
        Console.Write($"карт: {count:N0}. ");
    }

    /// <summary>
    /// Номер карты с заведомо несуществующим BIN 999999 и правильной контрольной цифрой Луна.
    /// Реальные BIN не используем, чтобы тестовые номера не совпали с картами настоящих банков.
    /// </summary>
    private string NewPan()
    {
        Span<char> d = stackalloc char[16];
        "999999".AsSpan().CopyTo(d);
        for (var k = 6; k < 15; k++) d[k] = (char)('0' + _rng.Next(10));
        d[15] = (char)('0' + Luhn.CheckDigit(d[..15]));
        return new string(d);
    }

    // ───────────────────────── Переводы ─────────────────────────

    private void SeedTransfers()
    {
        long total = o.Transfers;
        var spanTicks = (_end - _start).Ticks;
        var chunk = new List<Tx>(ChunkSize);
        var sw = Stopwatch.StartNew();

        for (long t = 0; t < total; t++)
        {
            // Равномерно по времени; порядок генерации = хронологический порядок.
            var at = _start.AddTicks((long)(spanTicks * ((double)t / total)));
            chunk.Add(NextTransfer(at));

            if (chunk.Count == ChunkSize || t == total - 1)
            {
                Flush(chunk);
                chunk.Clear();
                var done = t + 1;
                Console.Write($"\r  переводов: {done:N0} / {total:N0}, проводок: {done * 2:N0}  ({done / Math.Max(sw.Elapsed.TotalSeconds, 0.001):N0} переводов/с)   ");
            }
        }
        Console.WriteLine();
    }

    private Tx NextTransfer(DateTime at)
    {
        var c = _rng.Next(o.Clients);
        var debit = _debitOf[c];
        var r = _rng.NextDouble();

        if (r < 0.55) // покупка картой: деньги уходят из банка через корсчёт
        {
            var src = _creditOf[c] >= 0 && _rng.NextDouble() < 0.3 ? _creditOf[c] : debit;
            var amount = PurchaseAmount();
            if (Available(src) >= amount)
                return Make(src, Corr, amount, Pick(Names.Merchants), at);
        }
        else if (r < 0.75) // перевод другому клиенту Nova по номеру телефона
        {
            var c2 = _rng.Next(o.Clients - 1);
            if (c2 >= c) c2++;
            var amount = Money(100, 20_000, step: 10);
            if (Available(debit) >= amount)
                return Make(debit, _debitOf[c2], amount, "Перевод по номеру телефона", at);
        }
        else if (r < 0.83) // пополнение накопительного
        {
            var s = _savingsOf[c];
            var amount = Money(1_000, 50_000, step: 100);
            if (s >= 0 && Available(debit) >= amount)
                return Make(debit, s, amount, "Пополнение накопительного счёта", at);
        }
        else if (r < 0.87) // вывод с накопительного
        {
            var s = _savingsOf[c];
            var amount = Money(1_000, 30_000, step: 100);
            if (s >= 0 && _bal[s] >= amount)
                return Make(s, debit, amount, "Вывод с накопительного счёта", at);
        }
        else if (r < 0.92) // погашение кредитки
        {
            var cr = _creditOf[c];
            if (cr >= 0 && _bal[cr] < 0)
            {
                var amount = Math.Floor(Math.Min(-_bal[cr], Available(debit)) * 100m) / 100m;
                if (amount >= 1m)
                    return Make(debit, cr, amount, "Погашение кредитной карты", at);
            }
        }

        // Во всех остальных случаях (и если на операцию не хватило денег) — входящий платёж.
        return Make(Corr, debit, Money(15_000, 150_000, step: 100),
                    _rng.NextDouble() < 0.7 ? "Зарплата" : "Пополнение с карты другого банка", at);
    }

    private Tx Make(int from, int to, decimal amount, string comment, DateTime at)
    {
        _bal[from] -= amount;
        _bal[to] += amount;
        return new Tx(Guid.CreateVersion7(new DateTimeOffset(at)), from, to, amount, comment, at, _bal[from], _bal[to]);
    }

    private decimal Available(int acc) => _bal[acc] + _limit[acc];

    /// <summary>Покупки от 50 до 15 000 ₽, мелких заметно больше, чем крупных.</summary>
    private decimal PurchaseAmount()
    {
        double lo = Math.Log(50), hi = Math.Log(15_000);
        var x = Math.Exp(lo + Math.Pow(_rng.NextDouble(), 1.6) * (hi - lo));
        return Math.Round((decimal)x, 2);
    }

    private decimal Money(int min, int max, int step)
    {
        var v = min + _rng.NextDouble() * (max - min);
        return Math.Round((decimal)v / step) * step;
    }

    private void Flush(List<Tx> chunk)
    {
        // Сначала переводы, потом проводки: внешний ключ проводки → перевод должен находить строку.
        using (var imp = conn.BeginBinaryImport(
            "COPY transactions.transfers (id, idempotency_key, from_account_id, to_account_id, amount, currency, comment, status, failure_code, created_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var tx in chunk)
            {
                imp.StartRow();
                imp.Write(tx.Id, NpgsqlDbType.Uuid);
                imp.Write(Guid.NewGuid(), NpgsqlDbType.Uuid);
                imp.Write(_accId[tx.From], NpgsqlDbType.Uuid);
                imp.Write(_accId[tx.To], NpgsqlDbType.Uuid);
                imp.Write(tx.Amount, NpgsqlDbType.Numeric);
                imp.Write("RUB", NpgsqlDbType.Text);
                imp.Write(tx.Comment, NpgsqlDbType.Text);
                imp.Write(nameof(TransferStatus.Completed), NpgsqlDbType.Text);
                imp.WriteNull();
                imp.Write(tx.At, NpgsqlDbType.TimestampTz);
            }
            imp.Complete();
        }

        using (var imp = conn.BeginBinaryImport(
            "COPY transactions.ledger_entries (transfer_id, account_id, direction, amount, balance_after, created_at) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var tx in chunk)
            {
                WriteEntry(imp, tx.Id, _accId[tx.From], EntryDirection.Debit, tx.Amount, tx.FromAfter, tx.At);
                WriteEntry(imp, tx.Id, _accId[tx.To], EntryDirection.Credit, tx.Amount, tx.ToAfter, tx.At);
            }
            imp.Complete();
        }
    }

    private static void WriteEntry(NpgsqlBinaryImporter imp, Guid transferId, Guid accountId,
                                   EntryDirection dir, decimal amount, decimal after, DateTime at)
    {
        imp.StartRow();
        imp.Write(transferId, NpgsqlDbType.Uuid);
        imp.Write(accountId, NpgsqlDbType.Uuid);
        imp.Write(dir.ToString(), NpgsqlDbType.Text);
        imp.Write(amount, NpgsqlDbType.Numeric);
        imp.Write(after, NpgsqlDbType.Numeric);
        imp.Write(at, NpgsqlDbType.TimestampTz);
    }

    // ───────────────────────── Балансы, сотрудники, аудит ─────────────────────────

    private void WriteBalances()
    {
        Exec("CREATE TEMP TABLE tmp_balances (id uuid PRIMARY KEY, balance numeric(19,4) NOT NULL);");
        using (var imp = conn.BeginBinaryImport("COPY tmp_balances (id, balance) FROM STDIN (FORMAT BINARY)"))
        {
            for (var i = 0; i < _accCount; i++)
            {
                if (_bal[i] == 0m) continue;
                imp.StartRow();
                imp.Write(_accId[i], NpgsqlDbType.Uuid);
                imp.Write(_bal[i], NpgsqlDbType.Numeric);
            }
            imp.Complete();
        }
        var updated = Exec("UPDATE accounts.accounts a SET balance = t.balance FROM tmp_balances t WHERE a.id = t.id;");
        Exec("DROP TABLE tmp_balances;");
        Console.Write($"обновлено счетов: {updated:N0}. ");
    }

    private void SeedStaff()
    {
        (string Login, string Name, string Role)[] staff =
        [
            ("owner", "Пётр Бурименко", Roles.Owner),
            ("operator", "Игорь Ковалёв", Roles.Operator),
            ("support", "Мария Иванова", Roles.Support),
            ("credit", "Сергей Лебедев", Roles.CreditOfficer),
            ("risk", "Анна Смирнова", Roles.RiskManager),
            ("accountant", "Елена Козлова", Roles.Accountant),
            ("auditor", "Андрей Соколов", Roles.Auditor),
            ("sysadmin", "Дмитрий Орлов", Roles.SysAdmin),
        ];
        foreach (var (login, name, role) in staff)
        {
            var roleId = Roles.All.Single(r => r.Code == role).Id;
            using var cmd = new NpgsqlCommand("""
                INSERT INTO staff.staff (id, login, full_name, password_hash, role_id, is_active, totp_secret_enc, created_at)
                VALUES (@id, @login, @name, @hash, @role, true, NULL, @created)
                """, conn);
            cmd.Parameters.AddWithValue("id", Guid.CreateVersion7());
            cmd.Parameters.AddWithValue("login", login);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("hash", PasswordHasher.Hash(StaffPassword));
            cmd.Parameters.AddWithValue("role", roleId);
            cmd.Parameters.AddWithValue("created", DateTime.UtcNow);
            cmd.ExecuteNonQuery();
        }
        Console.Write($"сотрудников: {staff.Length}. ");
    }

    private void WriteAudit()
    {
        string prev;
        using (var q = new NpgsqlCommand("SELECT hash FROM audit.audit_log ORDER BY id DESC LIMIT 1", conn))
            prev = q.ExecuteScalar() as string ?? AuditLog.GenesisHash;

        var entry = new AuditLog
        {
            ActorType = ActorType.System,
            Action = "system.seed",
            Target = "database",
            PayloadJson = JsonSerializer.Serialize(new
            {
                clients = o.Clients, accounts = _accCount, transfers = o.Transfers,
                months = o.Months, seed = o.Seed,
            }),
            PrevHash = prev,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        entry.Seal();

        using var cmd = new NpgsqlCommand("""
            INSERT INTO audit.audit_log (actor_type, actor_id, action, target, payload_json, ip, created_at, prev_hash, hash)
            VALUES (@type, NULL, @action, @target, @payload, NULL, @created, @prev, @hash)
            """, conn);
        cmd.Parameters.AddWithValue("type", entry.ActorType.ToString());
        cmd.Parameters.AddWithValue("action", entry.Action);
        cmd.Parameters.AddWithValue("target", entry.Target!);
        cmd.Parameters.AddWithValue("payload", entry.PayloadJson!);
        cmd.Parameters.AddWithValue("created", entry.CreatedAt);
        cmd.Parameters.AddWithValue("prev", entry.PrevHash);
        cmd.Parameters.AddWithValue("hash", entry.Hash);
        cmd.ExecuteNonQuery();
    }

    private void Reconcile()
    {
        using (var cmd = new NpgsqlCommand(Sql.ReconcileAccounts, conn))
        using (var r = cmd.ExecuteReader())
        {
            r.Read();
            var totalAcc = r.GetInt64(0);
            var mismatched = r.GetInt64(1);
            Console.WriteLine();
            Console.WriteLine(mismatched == 0
                ? $"  ✔ Баланс = сумма проводок для всех {totalAcc:N0} счетов"
                : $"  ✘ РАСХОЖДЕНИЕ: {mismatched:N0} из {totalAcc:N0} счетов");
        }
        using (var cmd = new NpgsqlCommand(Sql.ReconcileLedger, conn))
        using (var r = cmd.ExecuteReader())
        {
            r.Read();
            var debits = r.GetDecimal(0);
            var credits = r.GetDecimal(1);
            Console.WriteLine(debits == credits
                ? $"  ✔ Дебет = кредит: {debits:N2} ₽ (двойная запись сходится)"
                : $"  ✘ Дебет {debits:N2} ≠ кредит {credits:N2}");
        }
    }

    // ───────────────────────── Утилиты ─────────────────────────

    private T Pick<T>(T[] items) => items[_rng.Next(items.Length)];

    private int Exec(string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        return cmd.ExecuteNonQuery();
    }

    private static void Step(string title, Action action)
    {
        Console.Write($"▸ {title}... ");
        var sw = Stopwatch.StartNew();
        action();
        Console.WriteLine($"[{sw.Elapsed.TotalSeconds:N1} с]");
    }
}
