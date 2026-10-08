using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nova.Infrastructure.Persistence;

/// <summary>Используется только командами dotnet ef (миграции) без запуска API.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NovaDbContext>
{
    public NovaDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("NOVA_DB")
                 ?? "Host=localhost;Port=5432;Database=nova;Username=nova;Password=nova_dev_password";
        var options = new DbContextOptionsBuilder<NovaDbContext>();
        DependencyInjection.Configure(options, cs);
        return new NovaDbContext(options.Options);
    }
}
