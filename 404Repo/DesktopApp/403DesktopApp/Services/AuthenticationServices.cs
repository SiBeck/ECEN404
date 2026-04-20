using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    public class AuthenticationService
    {
        /// <summary>
        /// The currently authenticated provider. Set after successful login.
        /// </summary>
        public static MedicalProvider? CurrentProvider { get; set; }

        /// <summary>
        /// Shared API client — callers (e.g. PatientService) attach the JWT
        /// to their requests via this instance.
        /// </summary>
        public static BioMetrixApiService Api { get; } = new BioMetrixApiService();

        private readonly PasswordHasher _passwordHasher;

        public AuthenticationService()
        {
            _passwordHasher = new PasswordHasher();
        }

        /// <summary>
        /// Authenticates a provider via the BioMetrixDatabase API.
        /// Falls back to offline local-credential check if the API is unreachable,
        /// so the app remains usable without network access during testing.
        /// Returns true and populates <see cref="CurrentProvider"/> on success.
        /// </summary>
        public async Task<bool> AuthenticateProviderAsync(string providerId, string password)
        {
            // ── 1. Try the live API ───────────────────────────────────────────
            try
            {
                var auth = await Api.LoginAsync(providerId, password);
                if (auth != null)
                {
                    CurrentProvider = new MedicalProvider
                    {
                        ProviderId = auth.User.ProviderId,
                        FirstName = auth.User.Name.Split(' ').FirstOrDefault() ?? "",
                        LastName = auth.User.Name.Contains(' ')
                            ? auth.User.Name[(auth.User.Name.IndexOf(' ') + 1)..]
                            : "",
                        Email = "",
                        Specialty = auth.User.Role,
                        IsActive = true
                    };
                    return true;
                }
                // API reachable but credentials rejected — no fallback.
                return false;
            }
            catch (HttpRequestException)
            {
                // API unreachable — fall through to offline check.
            }
            catch (TaskCanceledException)
            {
                // Timeout — fall through to offline check.
            }

            // ── 2. Offline fallback (demo credentials only) ───────────────────
            return AuthenticateProviderOffline(providerId, password);
        }

        /// <summary>
        /// Synchronous wrapper kept for UI code that cannot await.
        /// Prefer <see cref="AuthenticateProviderAsync"/> when possible.
        /// </summary>
        public bool AuthenticateProvider(string providerId, string password)
            => AuthenticateProviderOffline(providerId, password);

        private bool AuthenticateProviderOffline(string providerId, string password)
        {
            // Offline demo accounts — replace with a persistent local store for production.
            var offlineAccounts = GetOfflineDemoAccounts();
            var entry = offlineAccounts.FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                && p.IsActive);

            if (entry == null) return false;

            if (!_passwordHasher.VerifyPassword(password, entry.PasswordHash))
                return false;

            CurrentProvider = entry;
            return true;
        }

        public MedicalProvider? GetProvider(string providerId)
            => GetOfflineDemoAccounts().FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

        public string HashNewPassword(string password)
            => _passwordHasher.HashPassword(password);

        // Offline demo accounts are intentionally limited and clearly marked.
        // Production deployments must provision users through BioMetrixDatabase.
        private List<MedicalProvider> GetOfflineDemoAccounts()
        {
            return new List<MedicalProvider>
            {
                new() { ProviderId = "MD001", FirstName = "John",  LastName = "Smith",
                    Specialty = "Cardiology",     Email = "john.smith@demo.org",
                    PasswordHash = _passwordHasher.HashPassword("demo123"), IsActive = true },
                new() { ProviderId = "MD002", FirstName = "Sarah", LastName = "Johnson",
                    Specialty = "Pediatrics",     Email = "sarah.johnson@demo.org",
                    PasswordHash = _passwordHasher.HashPassword("demo456"), IsActive = true },
                new() { ProviderId = "NP001", FirstName = "Emily", LastName = "Davis",
                    Specialty = "Family Medicine", Email = "emily.davis@demo.org",
                    PasswordHash = _passwordHasher.HashPassword("demo789"), IsActive = true }
            };
        }
    }

    // PasswordHasher class unchanged...
    public class PasswordHasher
    {
        private const int SaltSize = 16; // 128 bits
        private const int HashSize = 32; // 256 bits
        private const int Iterations = 100000; // OWASP recommended minimum

        /// <summary>
        /// Creates a secure hash of the password using PBKDF2 with a random salt
        /// </summary>
        public string HashPassword(string password)
        {
            // Generate a random salt
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password with PBKDF2
            byte[] hash = HashPasswordWithSalt(password, salt);

            // Combine salt and hash for storage
            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            // Convert to base64 for storage
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// Verifies a password against a stored hash
        /// </summary>
        public bool VerifyPassword(string password, string storedHash)
        {
            // Convert stored hash from base64
            byte[] hashBytes = Convert.FromBase64String(storedHash);

            // Extract salt from stored hash
            byte[] salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            // Extract hash from stored hash
            byte[] storedPasswordHash = new byte[HashSize];
            Array.Copy(hashBytes, SaltSize, storedPasswordHash, 0, HashSize);

            // Hash the provided password with the extracted salt
            byte[] computedHash = HashPasswordWithSalt(password, salt);

            // Compare the hashes using timing-safe comparison
            return CryptographicOperations.FixedTimeEquals(computedHash, storedPasswordHash);
        }

        private byte[] HashPasswordWithSalt(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(HashSize);
            }
        }
    }
}