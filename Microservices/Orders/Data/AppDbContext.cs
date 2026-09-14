using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Orders
{
    public abstract class AppDbContext : DbContext
    {
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(e =>
            {
                e.HasKey(o => o.Id);
                e.Property(o => o.Total).HasPrecision(18, 2);
                e.Property(o => o.Extras).HasConversion(
                    new ValueConverter<List<string>, string>(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => string.IsNullOrEmpty(v)
                            ? new List<string>()
                            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()));
                e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            });

            modelBuilder.Entity<OrderItem>(e =>
            {
                e.HasKey(i => i.Id);
                e.Property(i => i.UnitPrice).HasPrecision(18, 2);
            });
        }
    }

    public class SqliteAppDbContext : AppDbContext
    {
        private readonly string _connectionString;

        public SqliteAppDbContext(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Default Timeout=5";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.IsDeleted).HasConversion<int>();
            });
        }
    }

    public class PostgresAppDbContext : AppDbContext
    {
        private const string LockTimeoutSuffix = ";Options=-c lock_timeout=1500";
        private readonly string _connectionString;

        public PostgresAppDbContext(string connectionString)
        {
            _connectionString = connectionString.Contains("Options=", StringComparison.Ordinal)
                ? connectionString
                : connectionString + LockTimeoutSuffix;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>(e =>
            {
                e.Property(o => o.OrderDate).HasConversion(v => ToUtc(v), v => FromUtc(v));
                e.Property(o => o.DeliveryDate).HasConversion(v => ToUtc(v), v => FromUtc(v));
            });
        }

        private static DateTime ToUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
        private static DateTime FromUtc(DateTime value) => value;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseNpgsql(_connectionString);
        }
    }
}
