using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedicalImagingAPI.Services
{
    public class DatabaseSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, IServiceProvider serviceProvider)
        {
            // Check if users already exist
            if (await context.Users.AnyAsync())
            {
                return;
            }

            var passwordHasher = serviceProvider.GetRequiredService<PasswordHasher>();

            var users = new List<User>
            {
                new User
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "MD001",
                    PasswordHash = passwordHasher.HashPassword("demo123"),
                    FirstName = "John",
                    LastName = "Smith",
                    Email = "john.smith@hospital.com",
                    Role = UserRole.Physician,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "MD002",
                    PasswordHash = passwordHasher.HashPassword("demo456"),
                    FirstName = "Sarah",
                    LastName = "Johnson",
                    Email = "sarah.johnson@hospital.com",
                    Role = UserRole.Physician,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "NP001",
                    PasswordHash = passwordHasher.HashPassword("demo789"),
                    FirstName = "Emily",
                    LastName = "Davis",
                    Email = "emily.davis@hospital.com",
                    Role = UserRole.Nurse,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "ADMIN",
                    PasswordHash = passwordHasher.HashPassword("admin123"),
                    FirstName = "System",
                    LastName = "Administrator",
                    Email = "admin@hospital.com",
                    Role = UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                // Service account used by PatientPortal for server-to-server calls.
                // Password is set via BioMetrixDatabase:ServiceAccountPassword in
                // PatientPortal appsettings; update here to match.
                new User
                {
                    Id = Guid.NewGuid(),
                    ProviderId = "PORTAL_SVC",
                    PasswordHash = passwordHasher.HashPassword("CHANGE_ME_IN_PRODUCTION"),
                    FirstName = "Patient",
                    LastName = "Portal Service",
                    Email = "portal-svc@hospital.internal",
                    Role = UserRole.Technician,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            context.Users.AddRange(users);
            await context.SaveChangesAsync();
        }
    }
}