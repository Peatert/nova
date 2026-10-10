namespace Nova.Seeder;

public sealed record SeedOptions
{
    public int Clients { get; init; } = 200_000;
    public int Transfers { get; init; } = 2_500_000; // ×2 проводки = 5 млн строк в ledger_entries
    public int Months { get; init; } = 18;
    public int Seed { get; init; } = 42;
    public bool Reset { get; init; }
    public bool Demo { get; init; }
    public string ConnectionString { get; init; } =
        Environment.GetEnvironmentVariable("NOVA_DB")
        ?? "Host=localhost;Port=5432;Database=nova;Username=nova;Password=nova_dev_password;Timeout=60;Command Timeout=0";
    public string EncryptionKey { get; init; } =
        Environment.GetEnvironmentVariable("NOVA_ENCRYPTION_KEY")
        ?? Infrastructure.Security.DevKeys.EncryptionKey;

    public static SeedOptions Parse(string[] args)
    {
        var o = new SeedOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Нет значения для {args[i]}");
            o = args[i] switch
            {
                "--clients" => o with { Clients = int.Parse(Next()) },
                "--transfers" => o with { Transfers = int.Parse(Next()) },
                "--months" => o with { Months = int.Parse(Next()) },
                "--seed" => o with { Seed = int.Parse(Next()) },
                "--reset" => o with { Reset = true },
                "--demo" => o with { Demo = true },
                "--connection" => o with { ConnectionString = Next() },
                "--help" or "-h" => throw new HelpRequested(),
                _ => throw new ArgumentException($"Неизвестный параметр: {args[i]}"),
            };
        }
        if (o.Clients < 2) throw new ArgumentException("--clients должно быть не меньше 2");
        if (o.Months < 1) throw new ArgumentException("--months должно быть не меньше 1");
        return o;
    }

    public const string Help = """
        Nova.Seeder — генератор тестовых данных.

          --clients N     клиентов (по умолчанию 200000)
          --transfers N   переводов, каждый = 2 проводки (по умолчанию 2500000)
          --months N      за сколько месяцев истории (по умолчанию 18)
          --seed N        зерно генератора, одинаковое зерно = одинаковые данные (42)
          --reset         очистить все данные перед генерацией (роли сохраняются)
          --demo          только добавить демо-клиенту +79990000000 насыщенную историю за 90 дней
          --connection S  строка подключения (или переменная окружения NOVA_DB)

        Пример быстрой проверки: dotnet run --project src/Nova.Seeder -c Release -- --clients 2000 --transfers 20000 --reset
        """;
}

public sealed class HelpRequested : Exception;
