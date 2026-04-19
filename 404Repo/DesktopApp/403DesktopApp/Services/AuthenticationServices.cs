using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    public class AuthenticationService
    {
        private const int IvSize = 16;

        private static readonly string StorageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BioMetrix");

        private static readonly string ProvidersFilePath =
            Path.Combine(StorageDir, "providers.enc");

        /// <summary>
        /// The currently authenticated provider. Set after successful login.
        /// </summary>
        public static MedicalProvider? CurrentProvider { get; set; }

        private readonly List<MedicalProvider> _providers;
        private readonly PasswordHasher _passwordHasher;

        public AuthenticationService()
        {
            _passwordHasher = new PasswordHasher();
            _providers = LoadOrSeedProviders();
        }

        public bool AuthenticateProvider(string providerId, string password)
        {
            var provider = _providers.FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                && p.IsActive);

            if (provider == null)
                return false;

            return _passwordHasher.VerifyPassword(password, provider.PasswordHash);
        }

        public MedicalProvider? GetProvider(string providerId)
        {
            return _providers.FirstOrDefault(p =>
                p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        }

        public string HashNewPassword(string password)
        {
            return _passwordHasher.HashPassword(password);
        }

        // ── Encrypted Provider Persistence ────────────────────────────────────

        private List<MedicalProvider> LoadOrSeedProviders()
        {
            Directory.CreateDirectory(StorageDir);

            if (File.Exists(ProvidersFilePath))
            {
                try
                {
                    byte[] ciphertext = File.ReadAllBytes(ProvidersFilePath);
                    byte[] plaintext = Decrypt(ciphertext);
                    string json = Encoding.UTF8.GetString(plaintext);
                    return JsonSerializer.Deserialize<List<MedicalProvider>>(json)
                           ?? SeedDefaultProviders();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AuthenticationService] Failed to load providers: {ex.Message}. Re-seeding.");
                    return SeedDefaultProviders();
                }
            }

            return SeedDefaultProviders();
        }

        private List<MedicalProvider> SeedDefaultProviders()
        {
            var defaults = new List<MedicalProvider>
            {
                new() {
                    ProviderId = "MD001", FirstName = "John", LastName = "Smith",
                    Specialty = "Cardiology",
                    PasswordHash = _passwordHasher.HashPassword("demo123"),
                    IsActive = true
                },
                new() {
                    ProviderId = "MD002", FirstName = "Sarah", LastName = "Johnson",
                    Specialty = "Pediatrics",
                    PasswordHash = _passwordHasher.HashPassword("demo456"),
                    IsActive = true
                },
                new() {
                    ProviderId = "NP001", FirstName = "Emily", LastName = "Davis",
                    Specialty = "Family Medicine",
                    PasswordHash = _passwordHasher.HashPassword("demo789"),
                    IsActive = true
                }
            };

            try
            {
                string json = JsonSerializer.Serialize(defaults);
                byte[] ciphertext = Encrypt(Encoding.UTF8.GetBytes(json));
                File.WriteAllBytes(ProvidersFilePath, ciphertext);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AuthenticationService] Could not persist providers: {ex.Message}");
            }

            return defaults;
        }

        // ── AES-256-CBC using the DPAPI-protected key (matches PatientService) ─

        private static byte[] Encrypt(byte[] plaintext)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = EncryptionKeyManager.GetOrCreateKey();
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            byte[] ct = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            byte[] result = new byte[IvSize + ct.Length];
            Array.Copy(aes.IV, 0, result, 0, IvSize);
            Array.Copy(ct, 0, result, IvSize, ct.Length);
            return result;
        }

        private static byte[] Decrypt(byte[] data)
        {
            if (data.Length < IvSize + 1)
                throw new CryptographicException("Encrypted data is too short.");

            byte[] iv = new byte[IvSize];
            Array.Copy(data, 0, iv, 0, IvSize);
            byte[] ct = new byte[data.Length - IvSize];
            Array.Copy(data, IvSize, ct, 0, ct.Length);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = EncryptionKeyManager.GetOrCreateKey();
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ct, 0, ct.Length);
        }
    }

    // ===================================
    // PasswordHasher — PBKDF2-SHA256
    // ===================================
    public class PasswordHasher
    {
        private const int SaltSize = 16;  // 128 bits
        private const int HashSize = 32;  // 256 bits
        private const int Iterations = 100_000;

        public string HashPassword(string password)
        {
            byte[] salt = new byte[SaltSize];
            RandomNumberGenerator.Fill(salt);

            byte[] hash = HashPasswordWithSalt(password, salt);

            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);
            return Convert.ToBase64String(hashBytes);
        }

        public bool VerifyPassword(string password, string storedHash)
        {
            byte[] hashBytes = Convert.FromBase64String(storedHash);

            byte[] salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            byte[] storedPasswordHash = new byte[HashSize];
            Array.Copy(hashBytes, SaltSize, storedPasswordHash, 0, HashSize);

            byte[] computedHash = HashPasswordWithSalt(password, salt);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedPasswordHash);
        }

        private static byte[] HashPasswordWithSalt(string password, byte[] salt)
        {
            using var pbkdf2 = new Rfc2898DeriveBytes(
                password, salt, Iterations, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(HashSize);
        }
    }
}
