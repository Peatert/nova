using Nova.Seeder;

namespace Nova.Tests;

public class ActivityTimelineTests
{
    private static readonly DateTime Start = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Time_is_monotonic_and_inside_range()
    {
        var tl = new ActivityTimeline(Start, End);
        var prev = DateTime.MinValue;
        for (var i = 0; i < 10_000; i++)
        {
            var at = tl.At((i + 0.5) / 10_000);
            Assert.True(at >= prev, "генератор обязан оставаться хронологическим");
            Assert.InRange(at, Start, End);
            prev = at;
        }
    }

    [Fact]
    public void Weekends_are_quieter_and_paydays_are_busier()
    {
        // 2026-03-04 — среда, 2026-03-08 — воскресенье, 2026-03-05 — день зарплаты (четверг).
        double Day(int d) => Enumerable.Range(0, 24)
            .Sum(h => ActivityTimeline.Weight(new DateTime(2026, 3, d, h, 0, 0, DateTimeKind.Utc).AddHours(-3), 0.5));

        Assert.True(Day(8) < Day(4) * 0.7);
        Assert.True(Day(5) > Day(4) * 1.4);
    }

    [Fact]
    public void Night_is_quieter_than_evening()
    {
        // 03:00 и 19:00 по Москве
        var night = ActivityTimeline.Weight(new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc), 0.5);
        var evening = ActivityTimeline.Weight(new DateTime(2026, 3, 4, 16, 0, 0, DateTimeKind.Utc), 0.5);
        Assert.True(evening > night * 10);
    }
}
