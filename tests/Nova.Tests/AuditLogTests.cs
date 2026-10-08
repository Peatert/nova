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
    public void Deleted_record_is_detected()
    {
        var chain = BuildChain(10);
        chain.RemoveAt(3);
        Assert.Equal(5, AuditLog.FindBrokenLink(chain));
    }
}
