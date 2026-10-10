using Nova.Domain;
using Nova.Domain.Entities;

namespace Nova.Tests;

public class AuditLogTests
{
    private static List<AuditLog> BuildChain(int count)
    {
        var list = new List<AuditLog>();
        var prev = AuditLog.GenesisHash;
        for (var i = 1; i <= count; i++)
        {
            var e = new AuditLog
            {
                Id = i, ActorType = ActorType.Staff, ActorId = Guid.NewGuid(),
                Action = "test.action", Target = $"target:{i}", PrevHash = prev,
                CreatedAt = new DateTimeOffset(2026, 10, 8, 12, 0, i, TimeSpan.Zero)
            };
            e.Seal();
            prev = e.Hash;
            list.Add(e);
        }
        return list;
    }

    [Fact]
    public void Intact_chain_passes()
    {
        Assert.Null(AuditLog.FindBrokenLink(BuildChain(10)));
    }

    [Fact]
    public void Edited_record_is_detected()
    {
        var chain = BuildChain(10);
        chain[4].Target = "подмена";
        Assert.Equal(5, AuditLog.FindBrokenLink(chain));
    }

    [Fact]
    public void Hash_survives_database_microsecond_precision()
    {
        // .NET хранит время до 100 нс, PostgreSQL — до 1 мкс. После чтения из БД хеш должен совпасть.
        var e = new AuditLog
        {
            ActorType = ActorType.System, Action = "system.seed", PrevHash = AuditLog.GenesisHash,
            CreatedAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567),
        };
        e.Seal();
        var fromDb = e.CreatedAt.AddTicks(-(e.CreatedAt.Ticks % 10)); // как вернёт PostgreSQL
        e.CreatedAt = fromDb;
        Assert.Equal(e.Hash, e.ComputeHash());
    }

    [Fact]
    public void Deleted_record_is_detected()
    {
        var chain = BuildChain(10);
        chain.RemoveAt(3);
        Assert.Equal(5, AuditLog.FindBrokenLink(chain));
    }
}
