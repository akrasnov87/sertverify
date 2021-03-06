using Microsoft.EntityFrameworkCore;
using SertCheck.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SertCheck
{
    public class ApplicationContext : DbContext
    {
        public ApplicationContext()
        {
            Database.EnsureCreated();
        }

        public DbSet<Document> Documents { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<File> Files { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql("Host=webdb;Port=5432;Database=vaccine-release-db;Username=vaccine-sert-verify;Password=J2Jbo6");
        }
    }
}
