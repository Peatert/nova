using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Nova.Infrastructure.Persistence;

namespace Nova.Admin;

/// <summary>Сырые SQL-запросы для отчётов, где LINQ неудобен (группировки по дням, системные каталоги).</summary>
public static class RawSql
{
    public static async Task<List<T>> QueryAsync<T>(NovaDbContext db, string sql, Func<DbDataReader, T> map)
    {
        var conn = db.Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var r = await cmd.ExecuteReaderAsync();
            var list = new List<T>();
            while (await r.ReadAsync()) list.Add(map(r));
            return list;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    /// <summary>Переводы по дням (по Москве) за последние 14 дней.</summary>
    public const string TransfersByDay = """
        SELECT (created_at AT TIME ZONE 'Europe/Moscow')::date AS day,
               count(*)                                      AS cnt,
               coalesce(sum(amount), 0)                      AS volume
        FROM transactions.transfers
        WHERE created_at >= now() - interval '14 days'
        GROUP BY 1
        ORDER BY 1
        """;
}

public static class Fmt
{
    private static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    public static string Money(decimal v) => (v < 0 ? "−" : "") + Math.Abs(v).ToString("N2", Ru) + " ₽";

    /// <summary>18 400 000 000 → «18,4 млрд ₽»: на дашборде важен порядок, а не копейки.</summary>
    public static string Short(decimal v) => Math.Abs(v) switch
    {
        >= 1_000_000_000m => (v / 1_000_000_000m).ToString("0.#", Ru) + " млрд ₽",
        >= 1_000_000m => (v / 1_000_000m).ToString("0.#", Ru) + " млн ₽",
        _ => Money(v),
    };

    public static string Num(long v) => v.ToString("N0", Ru);

    public static string Time(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM HH:mm:ss", Ru);

    public static string MaskPhone(string phone) =>
        phone.Length >= 12 ? phone[..5] + "***" + phone[^4..] : "***";
}
