using Microsoft.EntityFrameworkCore;

namespace Catalog
{
    public class AppDbContext : DbContext
    {
        private readonly string _connectionString;

        public AppDbContext(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Default Timeout=5";
        }

        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ReservationKey> ReservationKeys { get; set; } = null!;
        public DbSet<ReleaseKey> ReleaseKeys { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(e =>
            {
                e.HasKey(p => p.Id);
                e.Property(p => p.IsActive).HasConversion<int>();
                e.Property(p => p.IsDeleted).HasConversion<int>();
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
}
