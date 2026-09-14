using System;
using Microsoft.EntityFrameworkCore;

namespace Catalog
{
    public abstract class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ReservationKey> ReservationKeys { get; set; } = null!;
        public DbSet<ReleaseKey> ReleaseKeys { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(e =>
            {
                e.HasKey(p => p.Id);
                e.Property(p => p.Price).HasPrecision(18, 2);
                e.HasIndex(p => p.Category);
            });

            modelBuilder.Entity<ReservationKey>(e =>
            {
                e.HasKey(k => k.Key);
            });

            modelBuilder.Entity<ReleaseKey>(e =>
            {
                e.HasKey(k => k.Key);
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
            modelBuilder.Entity<Product>(e =>
            {
                e.Property(p => p.IsActive).HasConversion<int>();
                e.Property(p => p.IsDeleted).HasConversion<int>();
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
            modelBuilder.Entity<Product>(e =>
            {
                e.Property(p => p.AddedDate).HasConversion(v => ToUtc(v), v => FromUtc(v));
            });
            modelBuilder.Entity<ReservationKey>(e =>
            {
                e.Property(k => k.CreatedAt).HasConversion(v => ToUtc(v), v => FromUtc(v));
            });
            modelBuilder.Entity<ReleaseKey>(e =>
            {
                e.Property(k => k.CreatedAt).HasConversion(v => ToUtc(v), v => FromUtc(v));
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
