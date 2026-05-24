using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LegacyWebForms
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime AddedDate { get; set; }
    }

    [Serializable]
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal => Quantity * UnitPrice;
    }

    public class Order
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string Status { get; set; }
        public string Priority { get; set; }
        public List<string> Extras { get; set; } = new List<string>();
        public decimal Total { get; set; }
        public bool IsDeleted { get; set; }
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public static class AppData
    {
        private static string _cs;

        public static readonly List<string> Categories = new List<string>
        {
            "Electronics", "Clothing", "Food", "Books", "Sports"
        };

        public static void Initialize(string appDataPath)
        {
            Directory.CreateDirectory(appDataPath);
            string dbPath = Path.Combine(appDataPath, "inventory.db");
            _cs = $"Data Source={dbPath};Version=3;";
            using (var conn = Open())
            {
                CreateSchema(conn);
                if (!HasData(conn))
                    Seed(conn);
            }
        }

        // ── Products ──────────────────────────────────────────────────────────

        public static List<Product> GetProducts(bool includeDeleted = false)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = includeDeleted
                    ? "SELECT * FROM Products ORDER BY Id"
                    : "SELECT * FROM Products WHERE IsDeleted = 0 ORDER BY Id";
                return ReadProducts(cmd.ExecuteReader());
            }
        }

        public static Product GetProduct(int id)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Products WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var list = ReadProducts(cmd.ExecuteReader());
                return list.Count > 0 ? list[0] : null;
            }
        }

        public static int AddProduct(Product p)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Products (Name, Category, Price, Stock, IsActive, AddedDate)
                    VALUES (@n, @c, @p, @s, @a, @d);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@n", p.Name);
                cmd.Parameters.AddWithValue("@c", p.Category);
                cmd.Parameters.AddWithValue("@p", (double)p.Price);
                cmd.Parameters.AddWithValue("@s", p.Stock);
                cmd.Parameters.AddWithValue("@a", p.IsActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@d", DateTime.Today.ToString("yyyy-MM-dd"));
                return (int)(long)cmd.ExecuteScalar();
            }
        }

        public static void UpdateProduct(Product p)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    UPDATE Products
                    SET Name=@n, Category=@c, Price=@p, Stock=@s, IsActive=@a
                    WHERE Id=@id";
                cmd.Parameters.AddWithValue("@n", p.Name);
                cmd.Parameters.AddWithValue("@c", p.Category);
                cmd.Parameters.AddWithValue("@p", (double)p.Price);
                cmd.Parameters.AddWithValue("@s", p.Stock);
                cmd.Parameters.AddWithValue("@a", p.IsActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@id", p.Id);
                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteProduct(int id)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Products SET IsDeleted = 1, IsActive = 0 WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Orders ────────────────────────────────────────────────────────────

        public static List<Order> GetOrders(bool includeDeleted = false)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = includeDeleted
                    ? "SELECT * FROM Orders ORDER BY OrderDate DESC"
                    : "SELECT * FROM Orders WHERE IsDeleted = 0 ORDER BY OrderDate DESC";
                return ReadOrders(cmd.ExecuteReader());
            }
        }

        public static Order GetOrder(int id)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM Orders WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var list = ReadOrders(cmd.ExecuteReader());
                var order = list.Count > 0 ? list[0] : null;
                if (order != null)
                    order.Items = GetOrderItems(order.Id);
                return order;
            }
        }

        public static int AddOrder(Order o)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO Orders
                            (CustomerName, CustomerEmail, OrderDate, DeliveryDate, Status, Priority, Extras, Total)
                        VALUES
                            (@cn, @ce, @od, @dd, @st, @pr, @ex, @t);
                        SELECT last_insert_rowid();";
                    cmd.Transaction = tx;
                    cmd.Parameters.AddWithValue("@cn", o.CustomerName);
                    cmd.Parameters.AddWithValue("@ce", o.CustomerEmail);
                    cmd.Parameters.AddWithValue("@od", o.OrderDate.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@dd", o.DeliveryDate.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@st", o.Status);
                    cmd.Parameters.AddWithValue("@pr", o.Priority);
                    cmd.Parameters.AddWithValue("@ex", o.Extras != null ? string.Join(",", o.Extras) : "");
                    cmd.Parameters.AddWithValue("@t", (double)o.Total);
                    int orderId = (int)(long)cmd.ExecuteScalar();
                    if (o.Items != null && o.Items.Count > 0)
                    {
                        AddOrderItems(orderId, o.Items, conn, tx);
                        foreach (var item in o.Items)
                            DecrementStock(conn, tx, item.ProductId, item.Quantity);
                    }
                    tx.Commit();
                    return orderId;
                }
            }
        }

        public static void UpdateOrder(Order o)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Orders SET Status=@st, Priority=@pr WHERE Id=@id";
                cmd.Parameters.AddWithValue("@st", o.Status);
                cmd.Parameters.AddWithValue("@pr", o.Priority);
                cmd.Parameters.AddWithValue("@id", o.Id);
                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteOrder(int id)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction())
            {
                var items = GetOrderItems(id);
                foreach (var item in items)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE Products SET Stock = Stock + @qty, IsActive = 1 WHERE Id = @id";
                        cmd.Transaction = tx;
                        cmd.Parameters.AddWithValue("@qty", item.Quantity);
                        cmd.Parameters.AddWithValue("@id", item.ProductId);
                        cmd.ExecuteNonQuery();
                    }
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE Orders SET IsDeleted = 1 WHERE Id = @id";
                    cmd.Transaction = tx;
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        public static List<OrderItem> GetOrderItems(int orderId)
        {
            using (var conn = Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM OrderItems WHERE OrderId = @oid";
                cmd.Parameters.AddWithValue("@oid", orderId);
                return ReadOrderItems(cmd.ExecuteReader());
            }
        }

        public static void AddOrderItems(int orderId, List<OrderItem> items, SQLiteConnection conn = null, SQLiteTransaction tx = null)
        {
            bool ownTx = conn == null;
            if (ownTx) { conn = Open(); tx = conn.BeginTransaction(); }
            try
            {
                foreach (var item in items)
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT INTO OrderItems (OrderId, ProductId, ProductName, Quantity, UnitPrice)
                            VALUES (@oid, @pid, @pn, @qty, @u)";
                        cmd.Transaction = tx;
                        cmd.Parameters.AddWithValue("@oid", orderId);
                        cmd.Parameters.AddWithValue("@pid", item.ProductId);
                        cmd.Parameters.AddWithValue("@pn", item.ProductName);
                        cmd.Parameters.AddWithValue("@qty", item.Quantity);
                        cmd.Parameters.AddWithValue("@u", (double)item.UnitPrice);
                        cmd.ExecuteNonQuery();
                    }
                }
                if (ownTx) tx.Commit();
            }
            catch
            {
                if (ownTx) tx.Rollback();
                throw;
            }
            finally
            {
                if (ownTx) conn.Close();
            }
        }

        private static void DecrementStock(SQLiteConnection conn, SQLiteTransaction tx, int productId, int qty)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Products SET Stock = MAX(Stock - @qty, 0), IsActive = CASE WHEN MAX(Stock - @qty, 0) > 0 THEN IsActive ELSE 0 END WHERE Id = @id";
                cmd.Transaction = tx;
                cmd.Parameters.AddWithValue("@qty", qty);
                cmd.Parameters.AddWithValue("@id", productId);
                cmd.ExecuteNonQuery();
            }
        }

        public static string GetStatusBadge(string status)
        {
            if (status == "Deleted") return "<span class='badge badge-gray'>Deleted</span>";
            return status switch
            {
                "Delivered"  => "<span class='badge b-delivered'>Delivered</span>",
                "Shipped"    => "<span class='badge b-shipped'>Shipped</span>",
                "Processing" => "<span class='badge b-processing'>Processing</span>",
                _            => "<span class='badge b-pending'>Pending</span>",
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static SQLiteConnection Open()
        {
            var conn = new SQLiteConnection(_cs);
            conn.Open();
            return conn;
        }

        private static void CreateSchema(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Products (
                        Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name      TEXT    NOT NULL,
                        Category  TEXT    NOT NULL,
                        Price     REAL    NOT NULL,
                        Stock     INTEGER NOT NULL DEFAULT 0,
                        IsActive  INTEGER NOT NULL DEFAULT 1,
                        IsDeleted INTEGER NOT NULL DEFAULT 0,
                        AddedDate TEXT    NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS Orders (
                        Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                        CustomerName  TEXT    NOT NULL,
                        CustomerEmail TEXT    NOT NULL,
                        OrderDate     TEXT    NOT NULL,
                        DeliveryDate  TEXT    NOT NULL,
                        Status        TEXT    NOT NULL DEFAULT 'Pending',
                        Priority      TEXT    NOT NULL DEFAULT 'Normal',
                        Extras        TEXT,
                        Total         REAL    NOT NULL DEFAULT 0,
                        IsDeleted     INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS OrderItems (
                        Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                        OrderId   INTEGER NOT NULL,
                        ProductId INTEGER NOT NULL,
                        ProductName TEXT NOT NULL,
                        Quantity  INTEGER NOT NULL,
                        UnitPrice REAL NOT NULL
                    );";
                cmd.ExecuteNonQuery();
            }
        }

        private static bool HasData(SQLiteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Products";
                return (long)cmd.ExecuteScalar() > 0;
            }
        }

        private static void Seed(SQLiteConnection conn)
        {
            using (var tx = conn.BeginTransaction())
            {
                var products = new (string name, string cat, double price, int stock, int active, string date)[]
                {
                    ("Wireless Headphones",      "Electronics", 79.99,  42,  1, "2024-01-15"),
                    ("Mechanical Keyboard",      "Electronics", 129.99, 18,  1, "2024-02-03"),
                    ("USB-C Hub",                "Electronics", 39.99,  5,   1, "2024-03-10"),
                    ("Webcam HD",                "Electronics", 59.99,  0,   0, "2024-01-20"),
                    ("Dev T-Shirt (M)",          "Clothing",    24.99,  75,  1, "2024-02-28"),
                    ("Dev T-Shirt (L)",          "Clothing",    24.99,  60,  1, "2024-02-28"),
                    ("Hoodie (XL)",              "Clothing",    49.99,  3,   1, "2024-03-05"),
                    ("Clean Code",               "Books",       34.99,  20,  1, "2024-01-08"),
                    ("The Pragmatic Programmer", "Books",       39.99,  12,  1, "2024-01-08"),
                    ("Protein Bar (Box)",        "Food",        19.99,  200, 1, "2024-03-15"),
                    ("Ergonomic Mouse",          "Electronics", 49.99,  30,  1, "2024-04-01"),
                    ("Standing Desk Mat",        "Sports",      44.99,  8,   1, "2024-03-20"),
                };

                foreach (var p in products)
                    Exec(conn,
                        "INSERT INTO Products (Name, Category, Price, Stock, IsActive, AddedDate) VALUES (@n,@c,@p,@s,@a,@d)",
                        cmd => {
                            cmd.Parameters.AddWithValue("@n", p.name);
                            cmd.Parameters.AddWithValue("@c", p.cat);
                            cmd.Parameters.AddWithValue("@p", p.price);
                            cmd.Parameters.AddWithValue("@s", p.stock);
                            cmd.Parameters.AddWithValue("@a", p.active);
                            cmd.Parameters.AddWithValue("@d", p.date);
                        }, tx);

                var orders = new (string cn, string ce, string od, string dd, string st, string pr, string ex, double total)[]
                {
                    ("Alice Johnson", "alice@example.com",  "2024-05-01", "2024-05-07",  "Delivered",  "Normal", "Gift wrap", 159.98),
                    ("Bob Smith",     "bob@example.com",    "2024-05-03", "2024-05-10",  "Delivered",  "Low",    "", 34.99),
                    ("Carol White",   "carol@example.com",  "2024-05-10", "2024-05-17",  "Shipped",    "High",   "Express delivery", 129.99),
                    ("Dave Lee",      "dave@example.com",   "2024-05-12", "2024-05-20",  "Processing", "Normal", "", 74.97),
                    ("Eve Davis",     "eve@example.com",   "2024-05-14", "2024-05-22",  "Pending",    "Low",    "", 49.99),
                    ("Frank Miller",  "frank@example.com",  "2024-05-15", "2024-05-23",  "Pending",    "Normal", "Insurance", 79.98),
                    ("Grace Kim",     "grace@example.com",  "2024-05-16", "2024-05-24",  "Pending",    "High",   "Gift wrap,Express delivery", 204.96),
                    ("Henry Patel",   "henry@example.com",  "2024-05-18", "2024-05-26",  "Processing", "Normal", "", 69.98),
                    ("Iris Chen",     "iris@example.com",   "2024-05-20", "2024-05-28",  "Shipped",    "Low",    "Insurance", 39.99),
                    ("Jack Brown",    "jack@example.com",   "2024-05-22", "2024-05-30",  "Delivered",  "Normal", "", 129.98),
                    ("Karen Novak",   "karen@example.com",  "2024-05-25", "2024-06-02",  "Delivered",  "High",   "Gift wrap,Insurance", 174.97),
                    ("Leo Garcia",    "leo@example.com",    "2024-05-28", "2024-06-05",  "Processing", "Normal", "", 59.98),
                };

                var orderItems = new (int oid, int pid, string pn, int qty, double u)[]
                {
                    (1, 1, "Wireless Headphones", 2, 79.99),
                    (2, 8, "Clean Code",          1, 34.99),
                    (3, 2, "Mechanical Keyboard", 1, 129.99),
                    (4, 5, "Dev T-Shirt (M)",     3, 24.99),
                    (4, 7, "Hoodie (XL)",        1, 49.99),
                    (5, 11, "Ergonomic Mouse",     1, 49.99),
                    (6, 3, "USB-C Hub",           2, 39.99),
                    (7, 1, "Wireless Headphones", 1, 79.99),
                    (7, 11, "Ergonomic Mouse",    2, 49.99),
                    (7, 12, "Standing Desk Mat",  1, 44.99),
                    (8, 6, "Dev T-Shirt (L)",     2, 24.99),
                    (8, 9, "The Pragmatic Programmer", 1, 19.99),
                    (9, 4, "Webcam HD",            1, 39.99),
                    (10, 2, "Mechanical Keyboard", 1, 129.99),
                    (11, 8, "Clean Code",           1, 34.99),
                    (11, 9, "The Pragmatic Programmer", 1, 39.99),
                    (11, 12, "Standing Desk Mat", 2, 44.99),
                    (12, 3, "USB-C Hub",            1, 39.99),
                    (12, 10, "Protein Bar (Box)",   1, 19.99),
                };

                foreach (var o in orders)
                    Exec(conn,
                        "INSERT INTO Orders (CustomerName,CustomerEmail,OrderDate,DeliveryDate,Status,Priority,Extras,Total) VALUES (@cn,@ce,@od,@dd,@st,@pr,@ex,@t)",
                        cmd => {
                            cmd.Parameters.AddWithValue("@cn", o.cn);
                            cmd.Parameters.AddWithValue("@ce", o.ce);
                            cmd.Parameters.AddWithValue("@od", o.od);
                            cmd.Parameters.AddWithValue("@dd", o.dd);
                            cmd.Parameters.AddWithValue("@st", o.st);
                            cmd.Parameters.AddWithValue("@pr", o.pr);
                            cmd.Parameters.AddWithValue("@ex", o.ex);
                            cmd.Parameters.AddWithValue("@t", o.total);
                        }, tx);

                foreach (var oi in orderItems)
                    Exec(conn,
                        "INSERT INTO OrderItems (OrderId,ProductId,ProductName,Quantity,UnitPrice) VALUES (@oid,@pid,@pn,@qty,@u)",
                        cmd => {
                            cmd.Parameters.AddWithValue("@oid", oi.oid);
                            cmd.Parameters.AddWithValue("@pid", oi.pid);
                            cmd.Parameters.AddWithValue("@pn", oi.pn);
                            cmd.Parameters.AddWithValue("@qty", oi.qty);
                            cmd.Parameters.AddWithValue("@u", oi.u);
                        }, tx);

                tx.Commit();
            }
        }

        private static void Exec(SQLiteConnection conn, string sql, Action<SQLiteCommand> bind = null, SQLiteTransaction tx = null)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.Transaction = tx;
                bind?.Invoke(cmd);
                cmd.ExecuteNonQuery();
            }
        }

        private static List<Product> ReadProducts(SQLiteDataReader r)
        {
            var list = new List<Product>();
            using (r)
            {
                while (r.Read())
                    list.Add(new Product
                    {
                        Id        = r.GetInt32(r.GetOrdinal("Id")),
                        Name      = r.GetString(r.GetOrdinal("Name")),
                        Category  = r.GetString(r.GetOrdinal("Category")),
                        Price     = (decimal)r.GetDouble(r.GetOrdinal("Price")),
                        Stock     = r.GetInt32(r.GetOrdinal("Stock")),
                        IsActive  = r.GetInt32(r.GetOrdinal("IsActive")) == 1,
                        IsDeleted = r.GetInt32(r.GetOrdinal("IsDeleted")) == 1,
                        AddedDate = DateTime.Parse(r.GetString(r.GetOrdinal("AddedDate")), CultureInfo.InvariantCulture)
                    });
            }
            return list;
        }

        private static List<Order> ReadOrders(SQLiteDataReader r)
        {
            var list = new List<Order>();
            using (r)
            {
                while (r.Read())
                {
                    string extrasRaw = r.IsDBNull(r.GetOrdinal("Extras")) ? "" : r.GetString(r.GetOrdinal("Extras"));
                    list.Add(new Order
                    {
                        Id            = r.GetInt32(r.GetOrdinal("Id")),
                        CustomerName  = r.GetString(r.GetOrdinal("CustomerName")),
                        CustomerEmail = r.GetString(r.GetOrdinal("CustomerEmail")),
                        OrderDate     = DateTime.Parse(r.GetString(r.GetOrdinal("OrderDate")), CultureInfo.InvariantCulture),
                        DeliveryDate  = DateTime.Parse(r.GetString(r.GetOrdinal("DeliveryDate")), CultureInfo.InvariantCulture),
                        Status        = r.GetString(r.GetOrdinal("Status")),
                        Priority      = r.GetString(r.GetOrdinal("Priority")),
                        Extras        = string.IsNullOrEmpty(extrasRaw)
                            ? new List<string>()
                            : new List<string>(extrasRaw.Split(',')),
                        Total         = (decimal)r.GetDouble(r.GetOrdinal("Total")),
                        IsDeleted     = r.GetInt32(r.GetOrdinal("IsDeleted")) == 1
                    });
                }
            }
            return list;
        }

        private static List<OrderItem> ReadOrderItems(SQLiteDataReader r)
        {
            var list = new List<OrderItem>();
            using (r)
            {
                while (r.Read())
                    list.Add(new OrderItem
                    {
                        Id         = r.GetInt32(r.GetOrdinal("Id")),
                        OrderId    = r.GetInt32(r.GetOrdinal("OrderId")),
                        ProductId  = r.GetInt32(r.GetOrdinal("ProductId")),
                        ProductName = r.GetString(r.GetOrdinal("ProductName")),
                        Quantity   = r.GetInt32(r.GetOrdinal("Quantity")),
                        UnitPrice  = (decimal)r.GetDouble(r.GetOrdinal("UnitPrice"))
                    });
            }
            return list;
        }
    }
}
