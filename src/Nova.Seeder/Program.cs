using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;
using Nova.Seeder;

Console.OutputEncoding = Encoding.UTF8;

SeedOptions o;
try
{
    o = SeedOptions.Parse(args);
}
catch (HelpRequested)
{
    Console.WriteLine(SeedOptions.Help);
    return 0;
}
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(SeedOptions.Help);
    return 2;
}

Console.WriteLine($"Nova.Seeder: {o.Clients:N0} клиентов, {o.Transfers:N0} переводов, {o.Months} мес.");

Console.Write("▸ Миграции... ");
await using (var db = NovaDbContextFactory.Create(o.ConnectionString))
{
    await db.Database.MigrateAsync();
}
Console.WriteLine("ok");

await using var conn = new NpgsqlConnection(o.ConnectionString);
await conn.OpenAsync();

await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM clients.clients)", conn))
{
    var hasData = (bool)(await check.ExecuteScalarAsync())!;
    if (hasData && !o.Reset)
    {
        Console.Error.WriteLine("В базе уже есть данные. Запусти с --reset, чтобы очистить и сгенерировать заново.");
        return 1;
    }
}

if (o.Reset)
{
    Console.Write("▸ Очистка данных (роли сохраняются)... ");
    await using var cmd = new NpgsqlCommand("""
        TRUNCATE transactions.ledger_entries, transactions.transfers,
                 accounts.cards, accounts.accounts, clients.clients,
                 staff.staff, audit.audit_log
        RESTART IDENTITY;
        """, conn);
    await cmd.ExecuteNonQueryAsync();
    Console.WriteLine("ok");
}

new DataGenerator(o, conn, new FieldEncryptor(o.EncryptionKey)).Run();
return 0;
