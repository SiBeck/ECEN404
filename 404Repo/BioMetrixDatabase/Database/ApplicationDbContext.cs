using Microsoft.EntityFrameworkCore;
using MedicalImagingAPI.Database.Entities;

namespace MedicalImagingAPI.Database
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Patient> Patients { get; set; }
        public DbSet<DicomImage> DicomImages { get; set; }
        public DbSet<PatientFile> PatientFiles { get; set; }
        public DbSet<Annotation> Annotations { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.ProviderId).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.ProviderId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
            });

            // Patient configuration
            modelBuilder.Entity<Patient>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PatientId).IsUnique();
                entity.Property(e => e.PatientId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).HasMaxLength(255);
                entity.Property(e => e.Phone).HasMaxLength(20);
            });

            // DicomImage configuration
            modelBuilder.Entity<DicomImage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PatientId);
                entity.HasIndex(e => e.StudyId);
                entity.Property(e => e.FilePath).IsRequired();

                entity.HasOne(e => e.UploadedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.UploadedBy)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // PatientFile configuration
            modelBuilder.Entity<PatientFile>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PatientId);
                entity.HasIndex(e => new { e.PatientId, e.FileType });
                entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FilePath).IsRequired();

                entity.HasOne(e => e.Patient)
                      .WithMany(p => p.PatientFiles)
                      .HasForeignKey(e => e.PatientId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Annotation configuration
            modelBuilder.Entity<Annotation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.DicomImageId);

                entity.HasOne(e => e.DicomImage)
                      .WithMany(d => d.Annotations)
                      .HasForeignKey(e => e.DicomImageId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Creator)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedBy)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // AuditLog configuration
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => new { e.ResourceType, e.ResourceId });
            });
        }
    }
}