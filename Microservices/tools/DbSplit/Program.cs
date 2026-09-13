using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using CatalogContext = Catalog.AppDbContext;
using OrdersContext = Orders.AppDbContext;

if (args.Length != 6
    || args[0] != "--source" || args[2] != "--catalog" || args[4] != "--orders")
{
    Console.Error.WriteLine("Usage: DbSplit --source <shared.db> --catalog <catalog.db> --orders <orders.db>");
    return 2;
}

var sourcePath = args[1];
var catalogPath = args[3];
var ordersPath = args[5];

if (!File.Exists(sourcePath))
{
    Console.Error.WriteLine($"Source database not found: {sourcePath}");
    return 2;
}

new CatalogContext(catalogPath).Database.Migrate();
new OrdersContext(ordersPath).Database.Migrate();
Console.WriteLine($"Created schema: {catalogPath} (Catalog), {ordersPath} (Orders)");

var catalogTables = new[] { "Products", "ReservationKeys", "ReleaseKeys" };
var ordersTables = new[] { "Orders", "OrderItems" };

using (var source = new SqliteConnection($"Data Source={sourcePath}"))
{
    source.Open();
    using var catalogTarget = new SqliteConnection($"Data Source={catalogPath}");
    catalogTarget.Open();
    using var ordersTarget = new SqliteConnection($"Data Source={ordersPath}");
    ordersTarget.Open();

    foreach (var table in catalogTables)
        CopyTable(source, catalogTarget, table);
    foreach (var table in ordersTables)
        CopyTable(source, ordersTarget, table);
}

var ok = Verify(sourcePath, catalogPath, ordersPath);
return ok ? 0 : 1;

static void CopyTable(SqliteConnection source, SqliteConnection target, string table)
{
    using var read = new SqliteCommand($"SELECT * FROM {table}", source);
    using var reader = read.ExecuteReader();
    var columns = reader.GetColumnSchema().Select(c => c.ColumnName).ToList();
    var columnList = string.Join(", ", columns);
    var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));

    using var insertTx = target.BeginTransaction();
    long copied = 0;
    while (reader.Read())
    {
        using var insert = new SqliteCommand(
            $"INSERT INTO {table} ({columnList}) VALUES ({paramList})", target, insertTx);
        for (var i = 0; i < columns.Count; i++)
            insert.Parameters.AddWithValue($"@p{i}", reader.GetValue(i) ?? DBNull.Value);
        insert.ExecuteNonQuery();
        copied++;
    }
    insertTx.Commit();
    Console.WriteLine($"Copied {table}: {copied} rows");
}

static bool Verify(string sourcePath, string catalogPath, string ordersPath)
{
    var ok = true;

    using (var source = new SqliteConnection($"Data Source={sourcePath}"))
    using (var catalog = new SqliteConnection($"Data Source={catalogPath}"))
    using (var orders = new SqliteConnection($"Data Source={ordersPath}"))
    {
        source.Open();
        catalog.Open();
        orders.Open();

        foreach (var table in new[] { "Products", "ReservationKeys", "ReleaseKeys" })
            ok &= CompareCount(source, catalog, table);
        foreach (var table in new[] { "Orders", "OrderItems" })
            ok &= CompareCount(source, orders, table);

        using (var attach = new SqliteCommand("ATTACH DATABASE @catalog AS catalogDb", orders))
        {
            attach.Parameters.AddWithValue("@catalog", catalogPath);
            attach.ExecuteNonQuery();
        }
        using (var dangling = new SqliteCommand(
            "SELECT COUNT(*) FROM OrderItems oi WHERE NOT EXISTS (SELECT 1 FROM catalogDb.Products p WHERE p.Id = oi.ProductId)",
            orders))
        {
            var count = Convert.ToInt64(dangling.ExecuteScalar());
            Console.WriteLine($"OrderItem.ProductId values resolving against Catalog products: {(count == 0 ? "ALL" : $"{count} DANGLING")}");
            if (count > 0) ok = false;
        }
        using (var detach = new SqliteCommand("DETACH DATABASE catalogDb", orders))
        {
            detach.ExecuteNonQuery();
        }

        using (var stock = new SqliteCommand(
            "SELECT COALESCE(SUM(Stock), 0), COUNT(*) FROM Products", catalog))
        using (var reader = stock.ExecuteReader())
        {
            reader.Read();
            Console.WriteLine($"Catalog products: {reader.GetInt64(1)}, total stock units: {reader.GetInt64(0)}");
        }

        using (var totals = new SqliteCommand(
            "SELECT COUNT(*), COALESCE(SUM(IsDeleted), 0) FROM Orders", orders))
        using (var reader = totals.ExecuteReader())
        {
            reader.Read();
            Console.WriteLine($"Orders: {reader.GetInt64(0)} total, {reader.GetInt64(1)} soft-deleted");
        }
    }

    Console.WriteLine(ok ? "VERIFY OK" : "VERIFY FAILED");
    return ok;
}

static bool CompareCount(SqliteConnection source, SqliteConnection target, string table)
{
    using var s = new SqliteCommand($"SELECT COUNT(*) FROM {table}", source);
    using var t = new SqliteCommand($"SELECT COUNT(*) FROM {table}", target);
    var sc = Convert.ToInt64(s.ExecuteScalar());
    var tc = Convert.ToInt64(t.ExecuteScalar());
    Console.WriteLine($"{table}: source {sc}, target {tc} -> {(sc == tc ? "MATCH" : "MISMATCH")}");
    return sc == tc;
}
