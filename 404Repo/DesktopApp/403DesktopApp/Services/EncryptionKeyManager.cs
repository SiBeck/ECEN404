using System.IO;
using System.Security.Cryptography;

namespace _403DesktopApp.Services
{
    /// <summary>
    /// Manages the AES-256 encryption key used for all local PHI storage.
    /// On first run a cryptographically random 32-byte key is generated,
    /// protected with Windows DPAPI (bound to the current user account), and
    /// persisted to disk.  Subsequent runs load and unprotect the same key.
    /// </summary>
    public static class EncryptionKeyManager
    {
        private static readonly string KeyFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BioMetrix", "encryption.key");

        private static byte[]? _cachedKey;

        /// <summary>
        /// Returns the 32-byte AES key, creating and DPAPI-protecting it on first call.
        /// The key is cached in memory for the lifetime of the process.
        /// </summary>
        public static byte[] GetOrCreateKey()
        {
            if (_cachedKey != null)
                return _cachedKey;

            Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);

            if (File.Exists(KeyFilePath))
            {
                byte[] protectedBytes = File.ReadAllBytes(KeyFilePath);
                _cachedKey = ProtectedData.Unprotect(
                    protectedBytes, null, DataProtectionScope.CurrentUser);
                return _cachedKey;
            }

            // First run: generate a random 256-bit key
            byte[] key = new byte[32];
            RandomNumberGenerator.Fill(key);

            // Encrypt it so only this Windows user account can read it back
            byte[] protectedKey = ProtectedData.Protect(
                key, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(KeyFilePath, protectedKey);

            _cachedKey = key;
            return _cachedKey;
        }
    }
}
