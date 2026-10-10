using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Infrastructure.Persistence.Migrations;

/// <summary>
/// Переводит transactions.ledger_entries на партиционирование по месяцам (RANGE по created_at).
///
/// Миграция написана вручную: EF Core не умеет описывать партиции в модели.
/// Модель EF не меняется (EF по-прежнему считает ключом только id), поэтому снапшот не трогаем.
///
/// ВАЖНО: таблица пересоздаётся. Применять, пока в ней нет данных (наш случай — до генерации).
/// Для живой таблицы с данными делают иначе: новая партиционированная таблица + перенос пачками.
/// </summary>
[DbContext(typeof(NovaDbContext))]
[Migration("20261010115000_PartitionLedger")]
public partial class PartitionLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE transactions.ledger_entries;

            -- Первичный ключ партиционированной таблицы обязан включать ключ партиционирования.
            CREATE TABLE transactions.ledger_entries (
                id            bigint GENERATED ALWAYS AS IDENTITY,
                transfer_id   uuid           NOT NULL,
                account_id    uuid           NOT NULL,
                direction     varchar(32)    NOT NULL,
                amount        numeric(19,4)  NOT NULL,
                balance_after numeric(19,4)  NOT NULL,
                created_at    timestamptz    NOT NULL,
                CONSTRAINT pk_ledger_entries PRIMARY KEY (id, created_at),
                CONSTRAINT ck_ledger_amount_positive CHECK (amount > 0),
                CONSTRAINT fk_ledger_entries_transfers_transfer_id
                    FOREIGN KEY (transfer_id) REFERENCES transactions.transfers (id) ON DELETE RESTRICT,
                CONSTRAINT fk_ledger_entries_accounts_account_id
                    FOREIGN KEY (account_id) REFERENCES accounts.accounts (id) ON DELETE RESTRICT
            ) PARTITION BY RANGE (created_at);

            -- Индексы на родительской таблице автоматически создаются на каждой партиции.
            CREATE INDEX ix_ledger_entries_account_id_created_at
                ON transactions.ledger_entries (account_id, created_at DESC);
            CREATE INDEX ix_ledger_entries_transfer_id
                ON transactions.ledger_entries (transfer_id);

            -- Создание партиции на месяц. Вызывается миграцией и в будущем — планировщиком
            -- (раз в месяц создаём партицию на месяц вперёд).
            CREATE OR REPLACE FUNCTION transactions.create_ledger_partition(p_month date)
            RETURNS text
            LANGUAGE plpgsql AS $$
            DECLARE
                v_from timestamptz := date_trunc('month', p_month::timestamp) AT TIME ZONE 'UTC';
                v_to   timestamptz := (date_trunc('month', p_month::timestamp) + interval '1 month') AT TIME ZONE 'UTC';
                v_name text := format('ledger_entries_%s', to_char(p_month, 'YYYY_MM'));
            BEGIN
                EXECUTE format(
                    'CREATE TABLE IF NOT EXISTS transactions.%I PARTITION OF transactions.ledger_entries FOR VALUES FROM (%L) TO (%L)',
                    v_name, v_from, v_to);
                RETURN v_name;
            END;
            $$;

            SELECT transactions.create_ledger_partition(m::date)
            FROM generate_series(date '2024-01-01', date '2027-12-01', interval '1 month') AS m;

            -- Страховка: строки вне диапазона не теряются, а попадают сюда. В норме партиция пустая.
            CREATE TABLE transactions.ledger_entries_default
                PARTITION OF transactions.ledger_entries DEFAULT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE transactions.ledger_entries;
            DROP FUNCTION IF EXISTS transactions.create_ledger_partition(date);

            CREATE TABLE transactions.ledger_entries (
                id            bigint GENERATED ALWAYS AS IDENTITY,
                transfer_id   uuid           NOT NULL,
                account_id    uuid           NOT NULL,
                direction     varchar(32)    NOT NULL,
                amount        numeric(19,4)  NOT NULL,
                balance_after numeric(19,4)  NOT NULL,
                created_at    timestamptz    NOT NULL,
                CONSTRAINT pk_ledger_entries PRIMARY KEY (id),
                CONSTRAINT ck_ledger_amount_positive CHECK (amount > 0),
                CONSTRAINT fk_ledger_entries_transfers_transfer_id
                    FOREIGN KEY (transfer_id) REFERENCES transactions.transfers (id) ON DELETE RESTRICT,
                CONSTRAINT fk_ledger_entries_accounts_account_id
                    FOREIGN KEY (account_id) REFERENCES accounts.accounts (id) ON DELETE RESTRICT
            );
            CREATE INDEX ix_ledger_entries_account_id_created_at
                ON transactions.ledger_entries (account_id, created_at DESC);
            CREATE INDEX ix_ledger_entries_transfer_id
                ON transactions.ledger_entries (transfer_id);
            """);
    }
}
