using Microsoft.EntityFrameworkCore;
using Nova.Domain.Entities;

namespace Nova.Infrastructure.Persistence;

public class NovaDbContext(DbContextOptions<NovaDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Staff> StaffMembers => Set<Staff>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(NovaDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Деньги: numeric(19,4) везде, никаких float/double.
        c.Properties<decimal>().HavePrecision(19, 4);
        // Enum хранятся строками: читаемо в SQL и безопасно при добавлении новых значений.
        c.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }
}
