using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Orders
{
    public class AppDbContext : DbContext
    {
        private readonly string _connectionString;

        public AppDbContext(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};Default Timeout=5";
        }

        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseSqlite(_connectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
            });
        }
    }
}
