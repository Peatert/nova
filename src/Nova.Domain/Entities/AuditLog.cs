using System.Security.Cryptography;
using System.Text;

namespace Nova.Domain.Entities;

/// <summary>
/// Неизменяемый журнал действий. Записи связаны цепочкой хешей:
/// Hash = SHA256(PrevHash | поля записи). Удаление или правка любой записи ломает цепочку.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public ActorType ActorType { get; set; }
    public Guid? ActorId { get; set; }
    public required string Action { get; set; }
    public string? Target { get; set; }
    public string? PayloadJson { get; set; }
    public string? Ip { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public required string PrevHash { get; set; }
    public string Hash { get; set; } = "";

    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public string ComputeHash()
    {
        var raw = string.Join('|',
            PrevHash, ActorType, ActorId?.ToString() ?? "", Action, Target ?? "",
            PayloadJson ?? "", ToMicroseconds(CreatedAt).ToString("O"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public void Seal()
    {
        CreatedAt = ToMicroseconds(CreatedAt);
        Hash = ComputeHash();
    }

    /// <summary>
    /// PostgreSQL хранит время с точностью до микросекунды, .NET — до 100 нс.
    /// Без обрезки хеш, посчитанный до записи, не совпал бы с хешем после чтения из БД.
    /// </summary>
    public static DateTimeOffset ToMicroseconds(DateTimeOffset t)
    {
        var utc = t.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % 10));
    }

    /// <summary>Проверяет целостность цепочки. Возвращает Id первой испорченной записи или null.</summary>
    public static long? FindBrokenLink(IEnumerable<AuditLog> orderedById)
    {
        var expectedPrev = GenesisHash;
        foreach (var e in orderedById)
        {
            if (e.PrevHash != expectedPrev || e.Hash != e.ComputeHash())
                return e.Id;
            expectedPrev = e.Hash;
        }
        return null;
    }
}
