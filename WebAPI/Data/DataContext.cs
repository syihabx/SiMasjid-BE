using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebAPI.Controllers;
using WebAPI.Models;

namespace WebAPI.Data
{
    public class DataContext : IdentityDbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
        }

        public DbSet<FinancialReport> FinancialReports { get; set; }
        public DbSet<DailyTask> DailyTasks { get; set; }
        public DbSet<Inventory> Inventory { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }
}
