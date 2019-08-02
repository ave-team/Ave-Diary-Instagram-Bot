using DiaryInstaBot.Classes;
using DiaryInstaBot.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiaryInstaBot
{
    public class DatabaseContext : DbContext
    {
        public DbSet<Student> Students { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseMySQL(GetConnectionString());
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Student>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ClassLogin).IsRequired();
                entity.Property(e => e.ThreadId).IsRequired();
            });
        }

        private string GetConnectionString()
        {
            using (var reader = new StreamReader("settings.json"))
            {
                string json = reader.ReadToEnd();
                var settings = JsonConvert.DeserializeObject<BotSettings>(json);
                return settings.ConnectionString;
            }
        }
    }
}
