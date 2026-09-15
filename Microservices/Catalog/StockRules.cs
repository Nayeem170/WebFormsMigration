using Inventory.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalog;

// Extracted from Program.cs so the error-classification and lock-ordering
// rules reserve/release depend on can be unit-tested in-process instead of
// through full-stack failure injection.
internal static class StockRules
{
    public static bool IsUniqueViolation(DbUpdateException ex, string table) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505"
        || ex.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 19 && sqlite.Message.Contains(table);

    public static bool IsTransientLock(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg && pg.SqlState is "55P03" or "40P01" or "53300") return true;
            if (e is SqliteException sql && sql.SqliteErrorCode == 5) return true;
        }
        return false;
    }

    // Deadlock prevention: every transaction takes row locks in the same
    // ProductId order the FOR UPDATE list uses.
    public static List<StockItemDto> OrderItemsForLock(IEnumerable<StockItemDto> items) =>
        items.OrderBy(i => i.ProductId).ToList();

    public static List<int> OrderedLockIds(IEnumerable<StockItemDto> items) =>
        items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToList();
}
