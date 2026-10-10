namespace Nova.Infrastructure.Persistence;

/// <summary>Сырые SQL-запросы, общие для API и консольных утилит.</summary>
public static class Sql
{
    /// <summary>Сколько счетов всего и у скольких баланс не равен сумме проводок.</summary>
    public const string ReconcileAccounts = """
        WITH s AS (
            SELECT account_id,
                   SUM(CASE WHEN direction = 'Credit' THEN amount ELSE -amount END) AS total
            FROM transactions.ledger_entries
            GROUP BY account_id
        )
        SELECT COUNT(*)                                                  AS accounts,
               COUNT(*) FILTER (WHERE a.balance <> COALESCE(s.total, 0)) AS mismatched
        FROM accounts.accounts a
        LEFT JOIN s ON s.account_id = a.id
        """;

    /// <summary>Сумма дебетов и кредитов по всей книге. При двойной записи равны всегда.</summary>
    public const string ReconcileLedger = """
        SELECT COALESCE(SUM(amount) FILTER (WHERE direction = 'Debit'), 0)  AS debits,
               COALESCE(SUM(amount) FILTER (WHERE direction = 'Credit'), 0) AS credits
        FROM transactions.ledger_entries
        """;

    /// <summary>Партиции книги проводок: имя, оценка числа строк, размер на диске.</summary>
    public const string LedgerPartitions = """
        SELECT c.relname                              AS name,
               GREATEST(c.reltuples, 0)::bigint       AS rows,
               pg_total_relation_size(c.oid)          AS bytes
        FROM pg_inherits i
        JOIN pg_class c      ON c.oid = i.inhrelid
        JOIN pg_class p      ON p.oid = i.inhparent
        JOIN pg_namespace ns ON ns.oid = p.relnamespace
        WHERE ns.nspname = 'transactions' AND p.relname = 'ledger_entries'
        ORDER BY c.relname
        """;
}
