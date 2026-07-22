using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CoreWebForms
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
                    new ValueConverter<List<string>, string>(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                        v => string.IsNullOrEmpty(v)
                            ? new List<string>()
                            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()));
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
