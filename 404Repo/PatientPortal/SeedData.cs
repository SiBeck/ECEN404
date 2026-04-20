// Legacy copy of SeedData — moved to a distinct namespace to avoid duplicate type definitions.
// Prefer deleting this file entirely; this stub preserves history but prevents the ambiguous
// "SeedData.EnsureSeedDataAsync(ApplicationDbContext)" conflict at compile time.

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PatientPortal.Models;
using PatientPortal.Data;

namespace PatientPortal.Legacy
{
    [Obsolete("Legacy copy of SeedData. Use PatientPortal.Data.SeedData instead. Delete this file to remove duplication.")]
    public static class SeedData
    {
        // Example MRN used for the seeded demo account
        public const int DemoPatientId = 999999;

        // This stub throws if called so accidental calls are obvious at runtime.
        public static Task EnsureSeedDataAsync(ApplicationDbContext context)
        {
            throw new NotSupportedException("Call PatientPortal.Data.SeedData.EnsureSeedDataAsync instead. Delete PatientPortal\\SeedData.cs to remove this legacy copy.");
        }
    }
}