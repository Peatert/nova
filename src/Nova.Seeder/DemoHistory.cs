using Microsoft.EntityFrameworkCore;
using Nova.Domain;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Transfers;

namespace Nova.Seeder;

/// <summary>
/// Насыщенная история за последние 90 дней для демо-клиента — чтобы приложение на видео выглядело живым.
///
/// Переводы идут через настоящий TransferService (блокировки, двойная запись, проверка баланса),
/// а не прямой вставкой. Время подменяется: сервис получает часы, показывающие нужный момент в прошлом.
/// После генерации сверка «баланс = сумма проводок» по-прежнему сходится.
/// </summary>
internal static class DemoHistory
{
    public const string DemoPhone = "+79990000000";

    private sealed class ShiftedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly record struct Op(DateTimeOffset At, Guid From, Guid To, decimal Amount, string Comment);

    public static async Task<int> RunAsync(string connectionString, int seed)
    {
        Guid debit, corr;
        Guid? savings;
        List<Guid> friends;
        string name;

        await using (var db = NovaDbContextFactory.Create(connectionString))
        {
            var client = await db.Clients.AsNoTracking().SingleOrDefaultAsync(c => c.Phone == DemoPhone);
            if (client is null)
            {
                Console.Error.WriteLine($"Клиент {DemoPhone} не найден. Сначала сгенерируй данные: dotnet run --project src/Nova.Seeder -c Release");
                return 1;
            }
            name = client.FullName;

            var accounts = await db.Accounts.AsNoTracking().Where(a => a.ClientId == client.Id).ToListAsync();
            debit = accounts.First(a => a.Type == AccountType.Debit).Id;
            savings = accounts.FirstOrDefault(a => a.Type == AccountType.Savings)?.Id;
            corr = await db.Accounts.AsNoTracking().Where(a => a.Type == AccountType.Technical).Select(a => a.Id).FirstAsync();

            // «Друзья» — несколько постоянных получателей, как в реальной жизни.
            friends = await db.Accounts.AsNoTracking()
                .Where(a => a.Type == AccountType.Debit && a.ClientId != client.Id && a.Status == AccountStatus.Active)
                .OrderBy(a => a.Number)
                .Take(8)
                .Select(a => a.Id)
                .ToListAsync();
        }

        var ops = Plan(seed, debit, savings, corr, friends);
        Console.WriteLine($"Демо-история для {name} ({DemoPhone}): {ops.Count} операций за 90 дней");

        var clock = new ShiftedClock();
        int done = 0, skipped = 0;
        foreach (var op in ops)
        {
            clock.Now = op.At;
            await using var db = NovaDbContextFactory.Create(connectionString);
            var service = new TransferService(db, clock);
            try
            {
                await service.ExecuteAsync(new TransferCommand(op.From, op.To, op.Amount, op.Comment, Guid.NewGuid(), InitiatorClientId: null));
                done++;
            }
            catch (DomainException)
            {
                skipped++; // например, на покупку не хватило денег — в жизни так тоже бывает
            }
        }

        Console.WriteLine($"✔ Проведено {done}, пропущено {skipped} (не хватило средств)");
        return 0;
    }

    private static List<Op> Plan(int seed, Guid debit, Guid? savings, Guid corr, List<Guid> friends)
    {
        var rng = new Random(seed);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(-90);
        var ops = new List<Op>();

        DateTimeOffset At(DateTimeOffset day, int hourFrom, int hourTo) =>
            new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.FromHours(3))
                .AddHours(rng.Next(hourFrom, hourTo))
                .AddMinutes(rng.Next(0, 60))
                .ToUniversalTime();

        decimal Rub(double min, double max) => Math.Round((decimal)(min + rng.NextDouble() * (max - min)), 2);

        (string Name, double Min, double Max, int From, int To)[] shops =
        [
            ("Пятёрочка", 250, 2_500, 8, 22), ("ВкусВилл", 300, 3_000, 9, 22), ("Перекрёсток", 400, 4_000, 10, 22),
            ("Яндекс Такси", 180, 900, 7, 24), ("Метро Москвы", 64, 64, 7, 22), ("Самокат", 500, 2_500, 10, 23),
            ("Шоколадница", 250, 1_200, 8, 21), ("Кофе Хауз", 180, 600, 8, 20), ("Ozon", 400, 6_000, 10, 23),
            ("Wildberries", 300, 5_000, 10, 23), ("Аптека Ригла", 200, 1_800, 9, 21), ("Лукойл АЗС", 1_500, 3_500, 8, 21),
        ];

        for (var d = 0; d <= 90; d++)
        {
            var day = start.AddDays(d);

            // Зарплата 10-го и 25-го.
            if (day.Day is 10 or 25)
                ops.Add(new Op(At(day, 9, 11), corr, debit, Rub(78_000, 92_000), "Зарплата"));

            // 1–4 покупки в день.
            var purchases = rng.Next(1, 5);
            for (var i = 0; i < purchases; i++)
            {
                var s = shops[rng.Next(shops.Length)];
                ops.Add(new Op(At(day, s.From, s.To), debit, corr, Rub(s.Min, s.Max), s.Name));
            }

            // Переводы друзьям и от друзей.
            if (friends.Count > 0 && rng.NextDouble() < 0.25)
                ops.Add(new Op(At(day, 12, 23), debit, friends[rng.Next(friends.Count)],
                    Math.Round(Rub(300, 5_000) / 50) * 50, rng.NextDouble() < 0.5 ? "За обед" : "Перевод по номеру телефона"));
            if (friends.Count > 0 && rng.NextDouble() < 0.12)
                ops.Add(new Op(At(day, 12, 23), friends[rng.Next(friends.Count)], debit,
                    Math.Round(Rub(500, 4_000) / 50) * 50, "Перевод по номеру телефона"));

            // Раз в месяц — на накопительный.
            if (savings is { } sv && day.Day == 11)
                ops.Add(new Op(At(day, 12, 14), debit, sv, 15_000m, "Пополнение накопительного счёта"));
        }

        // Строго по времени и не в будущем.
        return ops.Where(o => o.At < now).OrderBy(o => o.At).ToList();
    }
}
