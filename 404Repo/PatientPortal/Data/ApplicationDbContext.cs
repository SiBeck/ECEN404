using Microsoft.EntityFrameworkCore;
using PatientPortal.Models;

namespace PatientPortal.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Patient> Patients { get; set; }
        public DbSet<PatientFile> PatientFiles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Patient configuration - primary key is 'PatientId' (int), external MRN is 'PatientId' (int)
            modelBuilder.Entity<Patient>(entity =>
            {
                entity.HasKey(e => e.PatientId);                    // PK: int PatientId
                entity.HasIndex(e => e.PatientId).IsUnique();       // external ID unique
                entity.Property(e => e.PatientId).IsRequired();
                entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            });

            // PatientFile configuration
            modelBuilder.Entity<PatientFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PatientId);
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FilePath).IsRequired();

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.PatientFiles)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
