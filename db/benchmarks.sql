-- ═══════════════════════════════════════════════════════════════════════════
-- Nova — замеры запросов. Запуск (PowerShell, из папки проекта):
--   docker cp db/benchmarks.sql nova-db:/tmp/benchmarks.sql
--   docker exec nova-db psql -U nova -d nova -f /tmp/benchmarks.sql -o /tmp/benchmarks-result.txt
--   docker cp nova-db:/tmp/benchmarks-result.txt .
-- Файл копируется через docker cp, а не через конвейер PowerShell: так не ломается кириллица.
-- Смотреть строки "Execution Time" и "Buffers".
-- Запрос 1 выполняется дважды: первый прогон прогревает кэш, честное время — у второго.
-- ═══════════════════════════════════════════════════════════════════════════

\timing off
\pset pager off

-- Тестовый счёт: самый активный обычный клиентский счёт (не технический корсчёт).
SELECT le.account_id AS acc
FROM transactions.ledger_entries le
JOIN accounts.accounts a ON a.id = le.account_id AND a.type = 'Debit'
WHERE le.created_at >= now() - interval '60 days'
GROUP BY le.account_id
ORDER BY count(*) DESC
LIMIT 1 \gset

-- Прошлый календарный месяц (для запроса оборотов).
SELECT date_trunc('month', now() - interval '1 month')::date AS m_from,
       date_trunc('month', now())::date                      AS m_to \gset

-- Тестовый телефон.
SELECT phone AS phone FROM clients.clients WHERE phone LIKE '+7999%' ORDER BY phone OFFSET 123456 LIMIT 1 \gset

\qecho
\qecho '═══ 0. Объём данных ═══'
SELECT (SELECT count(*) FROM clients.clients)               AS clients,
       (SELECT count(*) FROM accounts.accounts)             AS accounts,
       (SELECT count(*) FROM accounts.cards)                AS cards,
       (SELECT count(*) FROM transactions.transfers)        AS transfers,
       (SELECT count(*) FROM transactions.ledger_entries)   AS ledger_entries,
       pg_size_pretty(pg_database_size(current_database())) AS db_size;

\qecho
\qecho '═══ 1. Выписка: последние 50 операций счёта (С ИНДЕКСОМ) ═══'
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT created_at, direction, amount, balance_after
FROM transactions.ledger_entries
WHERE account_id = :'acc'
ORDER BY created_at DESC
LIMIT 50;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT created_at, direction, amount, balance_after
FROM transactions.ledger_entries
WHERE account_id = :'acc'
ORDER BY created_at DESC
LIMIT 50;

\qecho
\qecho '═══ 2. Та же выписка БЕЗ ИНДЕКСА (индекс удаляется внутри транзакции и возвращается ROLLBACK) ═══'
BEGIN;
DROP INDEX transactions.ix_ledger_entries_account_id_created_at;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT created_at, direction, amount, balance_after
FROM transactions.ledger_entries
WHERE account_id = :'acc'
ORDER BY created_at DESC
LIMIT 50;
ROLLBACK;

\qecho
\qecho '═══ 3. Обороты счёта за прошлый месяц: работает отсечение партиций (partition pruning) ═══'
\qecho '    В плане должна быть ОДНА партиция ledger_entries_ГГГГ_ММ, остальные не читаются.'
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT direction, count(*), sum(amount)
FROM transactions.ledger_entries
WHERE account_id = :'acc'
  AND created_at >= :'m_from' AND created_at < :'m_to'
GROUP BY direction;

\qecho
\qecho '═══ 4. Оборот всего банка за прошлый месяц (одна партиция целиком) ═══'
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT count(*), sum(amount)
FROM transactions.ledger_entries
WHERE created_at >= :'m_from' AND created_at < :'m_to';

\qecho
\qecho '═══ 5. Тот же оборот без фильтра по дате (для сравнения: читаются ВСЕ партиции) ═══'
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT count(*), sum(amount)
FROM transactions.ledger_entries;

\qecho
\qecho '═══ 6. Поиск клиента по телефону (уникальный индекс) ═══'
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id, full_name FROM clients.clients WHERE phone = :'phone';

\qecho
\qecho '═══ 7. Поиск по телефону БЕЗ ИНДЕКСА ═══'
BEGIN;
DROP INDEX IF EXISTS clients.ix_clients_phone;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id, full_name FROM clients.clients WHERE phone = :'phone';
ROLLBACK;

\qecho
\qecho '═══ 8. Партиции: строк и размер ═══'
SELECT c.relname AS partition,
       c.reltuples::bigint AS rows_estimate,
       pg_size_pretty(pg_total_relation_size(c.oid)) AS size
FROM pg_inherits i
JOIN pg_class c ON c.oid = i.inhrelid
JOIN pg_class p ON p.oid = i.inhparent
WHERE p.relname = 'ledger_entries' AND c.reltuples > 0
ORDER BY c.relname;
