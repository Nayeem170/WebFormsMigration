using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Npgsql;

if (args.Length != 8
    || args[0] != "--catalog-sqlite" || args[2] != "--orders-sqlite" || args[4] != "--catalog-pg" || args[6] != "--orders-pg")
{
    Console.Error.WriteLine("Usage: DbCopy --catalog-sqlite <catalog.db> --orders-sqlite <orders.db> --catalog-pg <conn> --orders-pg <conn>");
    return 2;
}

var catalogSqlite = args[1];
var ordersSqlite = args[3];
var catalogPg = args[5];
var ordersPg = args[7];

foreach (var path in new[] { catalogSqlite, ordersSqlite })
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Source database not found: {path}");
        return 2;
    }
}

var failures = 0;

using (var sqlite = OpenSqlite(catalogSqlite))
using (var pg = new NpgsqlConnection(catalogPg))
{
    pg.Open();
    using var tx = pg.BeginTransaction();

    Exec(pg, tx, @"TRUNCATE ""Products"", ""ReservationKeys"", ""ReleaseKeys"" RESTART IDENTITY");

    var products = new List<(int Id, string Name, string Category, decimal Price, int Stock, bool IsActive, bool IsDeleted, DateTime AddedDate)>();
    using (var read = Query(sqlite, @"SELECT ""Id"", ""Name"", ""Category"", ""Price"", ""Stock"", ""IsActive"", ""IsDeleted"", ""AddedDate"" FROM ""Products"""))
    {
        while (read.Read())
            products.Add((read.GetInt32(0), read.GetString(1), read.GetString(2), read.GetDecimal(3), read.GetInt32(4), read.GetBoolean(5), read.GetBoolean(6), read.GetDateTime(7)));
    }
    foreach (var p in products)
    {
        Exec(pg, tx, @"INSERT INTO ""Products"" (""Id"", ""Name"", ""Category"", ""Price"", ""Stock"", ""IsActive"", ""IsDeleted"", ""AddedDate"") VALUES (@id, @name, @category, @price, @stock, @isActive, @isDeleted, @addedDate)",
            ("@id", p.Id), ("@name", p.Name), ("@category", p.Category), ("@price", p.Price), ("@stock", p.Stock), ("@isActive", p.IsActive), ("@isDeleted", p.IsDeleted), ("@addedDate", p.AddedDate));
    }

    var reservationKeys = ReadKeys(sqlite, @"SELECT ""Key"", ""CreatedAt"" FROM ""ReservationKeys""");
    foreach (var k in reservationKeys)
        Exec(pg, tx, @"INSERT INTO ""ReservationKeys"" (""Key"", ""CreatedAt"") VALUES (@key, @createdAt)",
            ("@key", k.Key), ("@createdAt", k.CreatedAt));

    var releaseKeys = ReadKeys(sqlite, @"SELECT ""Key"", ""CreatedAt"" FROM ""ReleaseKeys""");
    foreach (var k in releaseKeys)
        Exec(pg, tx, @"INSERT INTO ""ReleaseKeys"" (""Key"", ""CreatedAt"") VALUES (@key, @createdAt)",
            ("@key", k.Key), ("@createdAt", k.CreatedAt));

    var maxProductId = products.Count == 0 ? 0 : products.Max(p => p.Id);
    ResetSequence(pg, tx, "Products", "Id", maxProductId);
    tx.Commit();

    VerifySequence(pg, "Products", "Id", maxProductId,
        @"INSERT INTO ""Products"" (""Name"", ""Category"", ""Price"", ""Stock"", ""IsActive"", ""IsDeleted"", ""AddedDate"") VALUES ('DbCopy sequence probe', 'Probe', 0, 0, false, true, @addedDate) RETURNING ""Id""",
        ("@addedDate", DateTime.UtcNow),
        @"DELETE FROM ""Products"" WHERE ""Id"" = @id AND ""Name"" = 'DbCopy sequence probe'");

    Console.WriteLine($"catalog: copied {products.Count} products, {reservationKeys.Count} reservation keys, {releaseKeys.Count} release keys");
}

using (var sqlite = OpenSqlite(ordersSqlite))
using (var pg = new NpgsqlConnection(ordersPg))
{
    pg.Open();
    using var tx = pg.BeginTransaction();

    Exec(pg, tx, @"TRUNCATE ""Orders"", ""OrderItems"" RESTART IDENTITY");

    var orders = new List<(int Id, string CustomerName, string CustomerEmail, DateTime OrderDate, DateTime DeliveryDate, string Status, string Priority, string Extras, decimal Total, bool IsDeleted)>();
    using (var read = Query(sqlite, @"SELECT ""Id"", ""CustomerName"", ""CustomerEmail"", ""OrderDate"", ""DeliveryDate"", ""Status"", ""Priority"", ""Extras"", ""Total"", ""IsDeleted"" FROM ""Orders"""))
    {
        while (read.Read())
            orders.Add((read.GetInt32(0), read.GetString(1), read.GetString(2), read.GetDateTime(3), read.GetDateTime(4), read.GetString(5), read.GetString(6), read.GetString(7), read.GetDecimal(8), read.GetBoolean(9)));
    }
    foreach (var o in orders)
    {
        Exec(pg, tx, @"INSERT INTO ""Orders"" (""Id"", ""CustomerName"", ""CustomerEmail"", ""OrderDate"", ""DeliveryDate"", ""Status"", ""Priority"", ""Extras"", ""Total"", ""IsDeleted"") VALUES (@id, @name, @email, @orderDate, @deliveryDate, @status, @priority, @extras, @total, @isDeleted)",
            ("@id", o.Id), ("@name", o.CustomerName), ("@email", o.CustomerEmail), ("@orderDate", o.OrderDate), ("@deliveryDate", o.DeliveryDate), ("@status", o.Status), ("@priority", o.Priority), ("@extras", o.Extras), ("@total", o.Total), ("@isDeleted", o.IsDeleted));
    }

    var items = new List<(int Id, int OrderId, int ProductId, string ProductName, int Quantity, decimal UnitPrice)>();
    using (var read = Query(sqlite, @"SELECT ""Id"", ""OrderId"", ""ProductId"", ""ProductName"", ""Quantity"", ""UnitPrice"" FROM ""OrderItems"""))
    {
        while (read.Read())
            items.Add((read.GetInt32(0), read.GetInt32(1), read.GetInt32(2), read.GetString(3), read.GetInt32(4), read.GetDecimal(5)));
    }
    foreach (var i in items)
    {
        Exec(pg, tx, @"INSERT INTO ""OrderItems"" (""Id"", ""OrderId"", ""ProductId"", ""ProductName"", ""Quantity"", ""UnitPrice"") VALUES (@id, @orderId, @productId, @productName, @quantity, @unitPrice)",
            ("@id", i.Id), ("@orderId", i.OrderId), ("@productId", i.ProductId), ("@productName", i.ProductName), ("@quantity", i.Quantity), ("@unitPrice", i.UnitPrice));
    }

    var maxOrderId = orders.Count == 0 ? 0 : orders.Max(o => o.Id);
    var maxItemId = items.Count == 0 ? 0 : items.Max(i => i.Id);
    ResetSequence(pg, tx, "Orders", "Id", maxOrderId);
    ResetSequence(pg, tx, "OrderItems", "Id", maxItemId);
    tx.Commit();

    VerifySequence(pg, "Orders", "Id", maxOrderId,
        @"INSERT INTO ""Orders"" (""CustomerName"", ""CustomerEmail"", ""OrderDate"", ""DeliveryDate"", ""Status"", ""Priority"", ""Extras"", ""Total"", ""IsDeleted"") VALUES ('DbCopy sequence probe', 'probe@example.com', @d, @d, 'Pending', 'Normal', '[]', 0, true) RETURNING ""Id""",
        ("@d", DateTime.UtcNow),
        @"DELETE FROM ""Orders"" WHERE ""Id"" = @id AND ""CustomerName"" = 'DbCopy sequence probe'");

    VerifySequence(pg, "OrderItems", "Id", maxItemId,
        @"INSERT INTO ""OrderItems"" (""OrderId"", ""ProductId"", ""ProductName"", ""Quantity"", ""UnitPrice"") VALUES (@orderId, 0, 'DbCopy sequence probe', 1, 0) RETURNING ""Id""",
        ("@orderId", maxOrderId),
        @"DELETE FROM ""OrderItems"" WHERE ""Id"" = @id AND ""ProductName"" = 'DbCopy sequence probe'");

    Console.WriteLine($"orders: copied {orders.Count} orders, {items.Count} order items");
}

Console.WriteLine("--- verification ---");

using (var sqlite = OpenSqlite(catalogSqlite))
using (var pg = new NpgsqlConnection(catalogPg))
{
    pg.Open();
    var sqliteSums = ReadSums(sqlite, @"SELECT COUNT(*), COALESCE(SUM(""Price""), 0), COALESCE(SUM(""Stock""), 0) FROM ""Products""");
    var pgSums = ReadSums(pg, @"SELECT COUNT(*), COALESCE(SUM(""Price""), 0), COALESCE(SUM(""Stock""), 0) FROM ""Products""");
    Check("product row count", sqliteSums.count == pgSums.count, $"{sqliteSums.count} vs {pgSums.count}");
    Check("SUM(Price)", sqliteSums.price == pgSums.price, $"{sqliteSums.price.ToString(CultureInfo.InvariantCulture)} vs {pgSums.price.ToString(CultureInfo.InvariantCulture)}");
    Check("SUM(Stock)", sqliteSums.stock == pgSums.stock, $"{sqliteSums.stock} vs {pgSums.stock}");
}

using (var sqlite = OpenSqlite(ordersSqlite))
using (var pg = new NpgsqlConnection(ordersPg))
{
    pg.Open();
    var sqliteSums = ReadSums(sqlite, @"SELECT COUNT(*), COALESCE(SUM(""Total""), 0), 0 FROM ""Orders""");
    var pgSums = ReadSums(pg, @"SELECT COUNT(*), COALESCE(SUM(""Total""), 0), 0 FROM ""Orders""");
    Check("order row count", sqliteSums.count == pgSums.count, $"{sqliteSums.count} vs {pgSums.count}");
    Check("SUM(Total)", sqliteSums.price == pgSums.price, $"{sqliteSums.price.ToString(CultureInfo.InvariantCulture)} vs {pgSums.price.ToString(CultureInfo.InvariantCulture)}");

    var sqliteItemCount = Convert.ToInt32(Scalar(sqlite, @"SELECT COUNT(*) FROM ""OrderItems"""));
    var pgItemCount = Convert.ToInt32(Scalar(pg, @"SELECT COUNT(*) FROM ""OrderItems"""));
    Check("order item row count", sqliteItemCount == pgItemCount, $"{sqliteItemCount} vs {pgItemCount}");

    Check("sqlite per-order Total equals sum of item lines", TotalsMatchItems(sqlite), "every order checked");
    Check("pg per-order Total equals sum of item lines", TotalsMatchItems(pg), "every order checked");
}

Console.WriteLine(failures == 0 ? "DbCopy: all checks passed" : $"DbCopy: {failures} check(s) FAILED");
return failures == 0 ? 0 : 1;

static SqliteConnection OpenSqlite(string path)
{
    var conn = new SqliteConnection($"Data Source={path}");
    conn.Open();
    return conn;
}

static IDbCommand Command(IDbConnection conn, IDbTransaction? tx, string sql, params (string Name, object Value)[] args)
{
    var cmd = conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    foreach (var (name, value) in args)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value switch
        {
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => value,
        };
        cmd.Parameters.Add(param);
    }
    return cmd;
}

static void Exec(NpgsqlConnection pg, NpgsqlTransaction tx, string sql, params (string, object)[] args)
{
    using var cmd = Command(pg, tx, sql, args);
    cmd.ExecuteNonQuery();
}

static SqliteDataReader Query(SqliteConnection conn, string sql)
{
    var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    return cmd.ExecuteReader();
}

static List<(string Key, DateTime CreatedAt)> ReadKeys(SqliteConnection conn, string sql)
{
    var keys = new List<(string, DateTime)>();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            keys.Add((reader.GetString(0), reader.GetDateTime(1)));
    }
    return keys;
}

static object Scalar(IDbConnection conn, string sql)
{
    using var cmd = Command(conn, null, sql);
    return cmd.ExecuteScalar()!;
}

static (int count, decimal price, int stock) ReadSums(IDbConnection conn, string sql)
{
    using var cmd = Command(conn, null, sql);
    using var reader = cmd.ExecuteReader();
    reader.Read();
    return (Convert.ToInt32(reader.GetValue(0)), Math.Round(Convert.ToDecimal(reader.GetValue(1)), 2), Convert.ToInt32(reader.GetValue(2)));
}

static bool TotalsMatchItems(IDbConnection conn)
{
    var orderTotals = new Dictionary<int, decimal>();
    using (var cmd = Command(conn, null, @"SELECT ""Id"", ""Total"" FROM ""Orders"""))
    using (var reader = cmd.ExecuteReader())
    {
        while (reader.Read())
            orderTotals[reader.GetInt32(0)] = reader.GetDecimal(1);
    }

    var itemTotals = new Dictionary<int, decimal>();
    using (var cmd = Command(conn, null, @"SELECT ""OrderId"", ""Quantity"" * ""UnitPrice"" FROM ""OrderItems"""))
    using (var reader = cmd.ExecuteReader())
    {
        while (reader.Read())
        {
            var orderId = reader.GetInt32(0);
            itemTotals.TryGetValue(orderId, out var sum);
            itemTotals[orderId] = sum + reader.GetDecimal(1);
        }
    }

    return orderTotals.All(kv => itemTotals.TryGetValue(kv.Key, out var sum) && Math.Round(sum, 2) == Math.Round(kv.Value, 2));
}

static void ResetSequence(NpgsqlConnection pg, NpgsqlTransaction tx, string table, string column, int maxValue)
{
    string seq;
    using (var cmd = Command(pg, tx, $"SELECT pg_get_serial_sequence('\"{table}\"', '{column}')"))
        seq = (string)cmd.ExecuteScalar()!;
    Exec(pg, tx, $"SELECT setval('{seq.Replace("'", "''")}', {maxValue.ToString(CultureInfo.InvariantCulture)}, true)");
}

void VerifySequence(NpgsqlConnection pg, string table, string column, int maxValue, string insertSql, (string, object) insertArg, string deleteSql)
{
    object insertedId;
    using (var cmd = Command(pg, null, insertSql, insertArg))
        insertedId = cmd.ExecuteScalar()!;
    var id = Convert.ToInt32(insertedId);
    Check($"{table} sequence advanced past copied max id", id > maxValue, $"inserted {id} > max {maxValue}");
    using (var cmd = Command(pg, null, deleteSql, ("@id", id)))
        cmd.ExecuteNonQuery();
}

void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} ({detail})");
    if (!ok) failures++;
}
