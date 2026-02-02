using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    public class AuthenticationService
    {
        private readonly List<MedicalProvider> _providers;
        private readonly PasswordHasher _passwordHasher;

        public AuthenticationService()
        {
            _passwordHasher = new PasswordHasher();

            // Demo providers with hashed passwords
            // In production, load from database
            _providers = new List<MedicalProvider>
            {
                new MedicalProvider
                {
                    ProviderId = "MD001",
                    FirstName = "John",
                    LastName = "Smith",
                    Specialty = "Cardiology",
                    // Password is "demo123" - hashed with PBKDF2
                    PasswordHash = _passwordHasher.HashPassword("demo123"),
                    IsActive = true
                },
                new MedicalProvider
                {
                    ProviderId = "MD002",
                    FirstName = "Sarah",
                    LastName = "Johnson",
                    Specialty = "Pediatrics",
                    // Password is "demo456"
                    PasswordHash = _passwordHasher.HashPassword("demo456"),
                    IsActive = true
                },
                new MedicalProvider
                {
                    ProviderId = "NP001",
                    FirstName = "Emily",
                    LastName = "Davis",
                    Specialty = "Family Medicine",
                    // Password is "demo789"
                    PasswordHash = _passwordHasher.HashPassword("demo789"),
                    IsActive = true
                }
            };
        }

        public bool AuthenticateProvider(string providerId, string password)
        {
            var provider = _providers.FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, System.StringComparison.OrdinalIgnoreCase)
                && p.IsActive);

            if (provider == null)
                return false;

            // Verify password using secure hash comparison
            return _passwordHasher.VerifyPassword(password, provider.PasswordHash);
        }

        public MedicalProvider GetProvider(string providerId)
        {
            return _providers.FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, System.StringComparison.OrdinalIgnoreCase));
        }

        // Method to hash a new password (for creating new providers)
        public string HashNewPassword(string password)
        {
            return _passwordHasher.HashPassword(password);
        }
    }
    // ===================================
    // PasswordHasher.cs - Secure Password Hashing using PBKDF2
    // ===================================
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