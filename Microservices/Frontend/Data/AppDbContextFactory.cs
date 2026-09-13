using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CoreWebForms
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "design.db");
            return new AppDbContext(dbPath);
        }
    }
}
