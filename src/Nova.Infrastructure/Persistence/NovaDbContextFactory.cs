using Microsoft.EntityFrameworkCore;

namespace Nova.Infrastructure.Persistence;

/// <summary>Создание контекста вне DI — для консольных утилит (Nova.Seeder).</summary>
public static class NovaDbContextFactory
{
    public static NovaDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NovaDbContext>();
        DependencyInjection.Configure(options, connectionString);
        return new NovaDbContext(options.Options);
    }
}
