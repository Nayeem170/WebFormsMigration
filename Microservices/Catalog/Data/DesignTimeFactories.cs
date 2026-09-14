using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog
{
    public class SqliteAppDbContextFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
    {
        public SqliteAppDbContext CreateDbContext(string[] args)
            => new(Path.Combine(Path.GetTempPath(), "ccw-catalog-design.db"));
    }

    public class PostgresAppDbContextFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
    {
        public PostgresAppDbContext CreateDbContext(string[] args)
            => new("Host=127.0.0.1;Port=15432;Database=ccw_catalog;Username=postgres;Password=localdev");
    }
}
