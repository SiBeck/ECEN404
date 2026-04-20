using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PatientPortal.Models;

namespace PatientPortal.Data
{
    /// <summary>
    /// Simple DB seed helper to create an example demo patient for local testing.
    /// Call SeedData.EnsureSeedDataAsync from your app startup (Program.cs) after the DI container is built.
    /// </summary>
    public static class SeedData
    {
        // Example MRN used for the seeded demo account
        public const int DemoPatientId = 999999;

        public static async Task EnsureSeedDataAsync(ApplicationDbContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // Ensure DB is created / migrations applied (optional depending on your startup flow)
            try
            {
                await context.Database.MigrateAsync();
            }
            catch
            {
                // Ignore migration errors in contexts where migrations are applied elsewhere.
            }

            // If demo account already exists do nothing
            if (await context.Patients.AnyAsync(p => p.PatientId == DemoPatientId))
                return;

            var demo = new Patient
            {
                PatientId = DemoPatientId,
                FirstName = "Demo",
                LastName = "Patient",
                DateOfBirth = new DateTime(1980, 1, 1),
                Email = "demo@example.com",
                Phone = "555-000-0000",
                // Hash the password at runtime using the same BCrypt call used elsewhere in the app
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("demo123"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null
            };

            context.Patients.Add(demo);
            await context.SaveChangesAsync();
        }
    }
}