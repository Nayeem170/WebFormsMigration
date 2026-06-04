using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace LegacyWebForms
{
    public class AppDbContext : DbContext
    {
        private readonly string _connectionString;

        public AppDbContext(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

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

            modelBuilder.Entity<Order>(e =>
            {
                e.HasKey(o => o.Id);
                e.Property(o => o.Extras).HasConversion(
                    v => string.Join("|", v),
                    v => string.IsNullOrEmpty(v) ? new List<string>() : v.Split('|').ToList());
                e.Property(o => o.IsDeleted).HasConversion<int>();
                e.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId);
            });

            modelBuilder.Entity<OrderItem>(e =>
            {
                e.HasKey(i => i.Id);
                e.Ignore(i => i.LineTotal);
            });
        }
    }
}
