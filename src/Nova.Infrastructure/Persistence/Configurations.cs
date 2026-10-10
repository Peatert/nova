using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Domain.Entities;
using Nova.Domain.Security;

namespace Nova.Infrastructure.Persistence;

// Схемы PostgreSQL по доменам: права можно выдавать на схему целиком
// (например, роли БД для бухгалтерии — только чтение схемы transactions).
internal static class Schemas
{
    public const string Clients = "clients";
    public const string Accounts = "accounts";
    public const string Transactions = "transactions";
    public const string Staff = "staff";
    public const string Audit = "audit";
    public const string Auth = "auth";
}

internal sealed class ClientConfig : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> e)
    {
        e.ToTable("clients", Schemas.Clients);
        e.Property(x => x.FullName).HasMaxLength(200);
        e.Property(x => x.Phone).HasMaxLength(16);
        e.HasIndex(x => x.Phone).IsUnique();
        e.Property(x => x.PasswordHash).HasMaxLength(200);
    }
}

internal sealed class AccountConfig : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> e)
    {
        e.ToTable("accounts", Schemas.Accounts, t =>
            t.HasCheckConstraint("ck_accounts_balance_limit", "balance >= -credit_limit"));
        e.Property(x => x.Number).HasMaxLength(20).IsFixedLength();
        e.HasIndex(x => x.Number).IsUnique();
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Version).IsRowVersion().HasColumnName("xmin"); // системный столбец PostgreSQL
        e.HasIndex(x => x.ClientId);
        e.HasOne(x => x.Client).WithMany(c => c.Accounts)
            .HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CardConfig : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> e)
    {
        e.ToTable("cards", Schemas.Accounts);
        e.Property(x => x.PanLast4).HasMaxLength(4).IsFixedLength();
        e.HasIndex(x => x.AccountId);
        e.HasOne(x => x.Account).WithMany(a => a.Cards)
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransferConfig : IEntityTypeConfiguration<Transfer>
{
    public void Configure(EntityTypeBuilder<Transfer> e)
    {
        e.ToTable("transfers", Schemas.Transactions, t =>
        {
            t.HasCheckConstraint("ck_transfers_amount_positive", "amount > 0");
            t.HasCheckConstraint("ck_transfers_different_accounts", "from_account_id <> to_account_id");
        });
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.FromAccountId, x.CreatedAt });
        e.HasIndex(x => new { x.ToAccountId, x.CreatedAt });
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Comment).HasMaxLength(140);
        e.Property(x => x.FailureCode).HasMaxLength(64);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.FromAccountId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Account>().WithMany().HasForeignKey(x => x.ToAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerEntryConfig : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> e)
    {
        e.ToTable("ledger_entries", Schemas.Transactions, t =>
            t.HasCheckConstraint("ck_ledger_amount_positive", "amount > 0"));
        e.Property(x => x.Id).UseIdentityAlwaysColumn();
        // Главный запрос выписки: проводки счёта по убыванию даты.
        e.HasIndex(x => new { x.AccountId, x.CreatedAt }).IsDescending(false, true);
        e.HasIndex(x => x.TransferId);
        e.HasOne<Transfer>().WithMany(t => t.Entries)
            .HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Account>().WithMany()
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StaffConfig : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> e)
    {
        e.ToTable("staff", Schemas.Staff);
        e.Property(x => x.Login).HasMaxLength(64);
        e.HasIndex(x => x.Login).IsUnique();
        e.Property(x => x.FullName).HasMaxLength(200);
        e.HasOne(x => x.Role).WithMany()
            .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfig : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> e)
    {
        e.ToTable("roles", Schemas.Staff);
        e.Property(x => x.Code).HasMaxLength(32);
        e.HasIndex(x => x.Code).IsUnique();
        e.Property(x => x.Name).HasMaxLength(100);
        e.HasData(Roles.All.Select(r => new Role
        {
            Id = r.Id, Code = r.Code, Name = r.Name, Permissions = r.Permissions
        }));
    }
}

internal sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> e)
    {
        e.ToTable("audit_log", Schemas.Audit);
        e.Property(x => x.Id).UseIdentityAlwaysColumn();
        e.Property(x => x.Action).HasMaxLength(64);
        e.Property(x => x.Target).HasMaxLength(128);
        // text, а не jsonb: jsonb переформатирует JSON, и хеш записи перестанет сходиться.
        e.Property(x => x.Ip).HasMaxLength(45);
        e.Property(x => x.PrevHash).HasMaxLength(64).IsFixedLength();
        e.Property(x => x.Hash).HasMaxLength(64).IsFixedLength();
        e.HasIndex(x => x.Hash).IsUnique();
        e.HasIndex(x => new { x.ActorId, x.CreatedAt });
    }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("refresh_tokens", Schemas.Auth);
        e.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength();
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => x.FamilyId);
        e.HasIndex(x => new { x.SubjectType, x.SubjectId });
    }
}
