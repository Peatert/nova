using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Nova.Infrastructure.Persistence;

/// <summary>
/// Имена в БД в snake_case (client_id, ix_accounts_client_id) — стандарт PostgreSQL.
/// Своя реализация вместо стороннего пакета: 20 строк, ноль зависимостей.
/// </summary>
internal static partial class SnakeCaseNaming
{
    public static void ApplySnakeCaseNames(this ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var p in entity.GetProperties())
            {
                // xmin — системный столбец PostgreSQL, имя задано явно, не трогаем.
                if (p.IsConcurrencyToken && p.ClrType == typeof(uint)) continue;
                p.SetColumnName(ToSnake(p.Name));
            }
            foreach (var k in entity.GetKeys())
                k.SetName(ToSnake(k.GetName()!));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnake(fk.GetConstraintName()!));
            foreach (var ix in entity.GetIndexes())
                ix.SetDatabaseName(ToSnake(ix.GetDatabaseName()!));
        }
    }

    public static string ToSnake(string name) =>
        Acronym().Replace(Boundary().Replace(name, "$1_$2"), "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Boundary();

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")]
    private static partial Regex Acronym();
}
