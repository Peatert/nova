using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nova.Infrastructure.Persistence;

namespace Nova.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNovaInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Nova")
                 ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Nova");
        services.AddDbContext<NovaDbContext>(o => Configure(o, cs));
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder o, string connectionString) =>
        o.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__ef_migrations", "public"));
}
