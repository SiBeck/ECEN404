using BioMetrixDatabase.Models;
using Microsoft.EntityFrameworkCore;

namespace BioMetrixDatabase.Data
{
    public class BioMetrixDbContext : DbContext
    {
        public BioMetrixDbContext(DbContextOptions<BioMetrixDbContext> options)
            : base(options) { }

        public DbSet<PatientEntity> Patients { get; set; }
        public DbSet<PatientImageEntity> PatientImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PatientEntity>()
                .HasKey(p => p.PatientId);

            modelBuilder.Entity<PatientImageEntity>()
                .HasKey(i => i.ImageId);

            modelBuilder.Entity<PatientImageEntity>()
                .HasOne(i => i.Patient)
                .WithMany(p => p.Images)
                .HasForeignKey(i => i.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
