using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _403DesktopApp.Models;

namespace _403DesktopApp.Services
{
    /// <summary>
    /// Manages patient profiles with AES-256-CBC encrypted local storage.
    /// Patient data is serialized to JSON, encrypted, and stored in the
    /// local application data directory (%APPDATA%/BioMetrix/patients/).
    /// Each patient record is a separate encrypted file keyed by PatientId.
    /// </summary>
    public class PatientService
    {
        private const int KeySize = 32;       // AES-256
        private const int IvSize = 16;        // AES block size
        private const int SaltSize = 16;      // PBKDF2 salt
        private const int Iterations = 100_000; // OWASP-compliant PBKDF2 iterations

        private readonly string _storageDir;
        private readonly byte[] _encryptionKey;

        /// <summary>
        /// Initializes the patient service. Derives an AES-256 encryption key
        /// from the given passphrase using PBKDF2-SHA256.
        /// </summary>
        /// <param name="passphrase">
        /// Passphrase used to derive the encryption key. In production this
        /// should come from a secure key store or hardware security module.
        /// </param>
        public PatientService(string passphrase = "BioMetrix-PHI-Encryption-Key")
        {
            _storageDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BioMetrix", "patients");
            Directory.CreateDirectory(_storageDir);

            // Derive a stable key from the passphrase using a fixed salt.
            // The fixed salt ensures the same key is derived across sessions.
            byte[] fixedSalt = Encoding.UTF8.GetBytes("BioMetrix-Salt-v1");
            using var kdf = new Rfc2898DeriveBytes(
                passphrase, fixedSalt, Iterations, HashAlgorithmName.SHA256);
            _encryptionKey = kdf.GetBytes(KeySize);
        }

        /// <summary>
        /// Validates a patient profile before saving.
        /// Returns null if valid, or an error message describing the problem.
        /// </summary>
        public static string? ValidateProfile(PatientProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.FirstName))
                return "First name is required.";
            if (string.IsNullOrWhiteSpace(profile.LastName))
                return "Last name is required.";
            if (profile.DateOfBirth > DateTime.Today)
                return "Date of birth cannot be in the future.";
            if (profile.DateOfBirth < new DateTime(1900, 1, 1))
                return "Date of birth is not valid.";
            if (!string.IsNullOrWhiteSpace(profile.Email) && !profile.Email.Contains('@'))
                return "Email address format is invalid.";
            if (!string.IsNullOrWhiteSpace(profile.PhoneNumber) &&
                profile.PhoneNumber.Any(c => !char.IsDigit(c) && c != '-' && c != '(' && c != ')' && c != ' ' && c != '+'))
                return "Phone number contains invalid characters.";
            return null;
        }

        /// <summary>
        /// Saves a patient profile to encrypted local storage.
        /// Creates a new file or overwrites an existing one.
        /// </summary>
        public void SavePatient(PatientProfile profile)
        {
            string error = ValidateProfile(profile);
            if (error != null)
                throw new ArgumentException(error);

            profile.LastModifiedDate = DateTime.UtcNow;

            string json = JsonSerializer.Serialize(profile, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            byte[] plaintext = Encoding.UTF8.GetBytes(json);
            byte[] ciphertext = Encrypt(plaintext);

            string filePath = GetPatientFilePath(profile.PatientId);
            File.WriteAllBytes(filePath, ciphertext);
        }

        /// <summary>
        /// Loads a patient profile by ID from encrypted storage.
        /// Returns null if the patient is not found.
        /// </summary>
        public PatientProfile? LoadPatient(string patientId)
        {
            string filePath = GetPatientFilePath(patientId);
            if (!File.Exists(filePath))
                return null;

            byte[] ciphertext = File.ReadAllBytes(filePath);
            byte[] plaintext = Decrypt(ciphertext);
            string json = Encoding.UTF8.GetString(plaintext);

            return JsonSerializer.Deserialize<PatientProfile>(json);
        }

        /// <summary>
        /// Loads all patient profiles from encrypted storage.
        /// Skips any files that fail to decrypt (corrupted/tampered).
        /// </summary>
        public List<PatientProfile> LoadAllPatients()
        {
            var patients = new List<PatientProfile>();

            if (!Directory.Exists(_storageDir))
                return patients;

            foreach (var file in Directory.GetFiles(_storageDir, "*.enc"))
            {
                try
                {
                    byte[] ciphertext = File.ReadAllBytes(file);
                    byte[] plaintext = Decrypt(ciphertext);
                    string json = Encoding.UTF8.GetString(plaintext);
                    var profile = JsonSerializer.Deserialize<PatientProfile>(json);
                    if (profile != null)
                        patients.Add(profile);
                }
                catch (CryptographicException)
                {
                    // Skip corrupted or tampered files
                    System.Diagnostics.Debug.WriteLine(
                        $"[PatientService] Skipping corrupted file: {Path.GetFileName(file)}");
                }
            }

            return patients.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ToList();
        }

        /// <summary>
        /// Deletes a patient profile from encrypted storage.
        /// Returns true if the file was found and deleted.
        /// </summary>
        public bool DeletePatient(string patientId)
        {
            string filePath = GetPatientFilePath(patientId);
            if (!File.Exists(filePath))
                return false;

            File.Delete(filePath);
            AuditLogger.Log(
                AuthenticationService.CurrentProvider?.ProviderId ?? "UNKNOWN",
                "DELETE", "Patient", patientId);
            return true;
        }

        /// <summary>
        /// Searches patients by name or medical record number (case-insensitive).
        /// </summary>
        public List<PatientProfile> SearchPatients(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return LoadAllPatients();

            string q = query.Trim().ToLowerInvariant();
            return LoadAllPatients()
                .Where(p =>
                    p.FirstName.ToLowerInvariant().Contains(q) ||
                    p.LastName.ToLowerInvariant().Contains(q) ||
                    p.MedicalRecordNumber.ToLowerInvariant().Contains(q) ||
                    p.FullName.ToLowerInvariant().Contains(q))
                .ToList();
        }

        /// <summary>
        /// Saves an image file into the patient's managed image directory,
        /// records the metadata in the patient profile, and re-encrypts the record.
        /// Returns the PatientImage metadata that was created.
        /// </summary>
        public PatientImage SaveImageForPatient(
            string patientId,
            string sourceFilePath,
            string tag,
            string description,
            string sourceType)
        {
            var profile = LoadPatient(patientId)
                ?? throw new InvalidOperationException($"Patient '{patientId}' not found.");

            string imageDir = GetPatientImageDir(patientId);
            Directory.CreateDirectory(imageDir);

            string ext = Path.GetExtension(sourceFilePath);
            var image = new PatientImage
            {
                FileName = Path.GetFileName(sourceFilePath),
                Tag = tag?.Trim() ?? "",
                Description = description?.Trim() ?? "",
                SourceType = sourceType ?? "",
                SavedDate = DateTime.UtcNow,
                SavedByProviderId =
                    AuthenticationService.CurrentProvider?.ProviderId ?? "UNKNOWN"
            };

            string destPath = Path.Combine(imageDir, $"{image.ImageId}{ext}");
            File.Copy(sourceFilePath, destPath, overwrite: true);

            // Store relative filename so records are portable
            image.FileName = $"{image.ImageId}{ext}";

            profile.AssociatedImages.Add(image);
            profile.LastModifiedDate = DateTime.UtcNow;
            SavePatient(profile);

            return image;
        }

        /// <summary>
        /// Removes an image from a patient profile and deletes the file.
        /// </summary>
        public void RemoveImageFromPatient(string patientId, string imageId)
        {
            var profile = LoadPatient(patientId);
            if (profile == null) return;

            var image = profile.AssociatedImages.FirstOrDefault(i => i.ImageId == imageId);
            if (image == null) return;

            string filePath = GetImageFullPath(patientId, image.FileName);
            if (File.Exists(filePath))
                File.Delete(filePath);

            profile.AssociatedImages.Remove(image);
            profile.LastModifiedDate = DateTime.UtcNow;
            SavePatient(profile);

            AuditLogger.Log(
                AuthenticationService.CurrentProvider?.ProviderId ?? "UNKNOWN",
                "DELETE", "PatientImage", $"{patientId}/{imageId}");
        }

        /// <summary>
        /// Saves a list of files into the patient's managed image directory as a series,
        /// records metadata for each, attaches filter stats to the first image, and
        /// cleans up the source temp directory when done.
        /// </summary>
        public List<PatientImage> SaveImageSeriesForPatient(
            string patientId,
            IEnumerable<string> sourceFilePaths,
            string seriesTag,
            string description,
            string sourceType,
            PatientImage.ScanFilterStats? filterStats = null)
        {
            var profile = LoadPatient(patientId)
                ?? throw new InvalidOperationException($"Patient '{patientId}' not found.");

            string imageDir = GetPatientImageDir(patientId);
            Directory.CreateDirectory(imageDir);

            var savedImages = new List<PatientImage>();
            string? tempDirToClean = null;
            bool isFirst = true;

            foreach (var sourceFilePath in sourceFilePaths)
            {
                string ext = Path.GetExtension(sourceFilePath);
                var image = new PatientImage
                {
                    Tag = seriesTag?.Trim() ?? "",
                    Description = description?.Trim() ?? "",
                    SourceType = sourceType ?? "",
                    SavedDate = DateTime.UtcNow,
                    SavedByProviderId =
                        AuthenticationService.CurrentProvider?.ProviderId ?? "UNKNOWN",
                    FilterStats = isFirst ? filterStats : null
                };

                string destPath = Path.Combine(imageDir, $"{image.ImageId}{ext}");
                File.Copy(sourceFilePath, destPath, overwrite: true);
                image.FileName = $"{image.ImageId}{ext}";

                profile.AssociatedImages.Add(image);
                savedImages.Add(image);

                if (tempDirToClean == null)
                {
                    string? dir = Path.GetDirectoryName(sourceFilePath);
                    if (dir != null && Path.GetFileName(dir).StartsWith("BioMetrix_"))
                        tempDirToClean = dir;
                }

                isFirst = false;
            }

            profile.LastModifiedDate = DateTime.UtcNow;
            SavePatient(profile);

            // Remove temp output dir now that all files are safely in managed storage
            if (tempDirToClean != null && Directory.Exists(tempDirToClean))
            {
                try { Directory.Delete(tempDirToClean, recursive: true); }
                catch { /* non-fatal */ }
            }

            return savedImages;
        }

        /// <summary>
        /// Updates the tag and description of an existing patient image.
        /// </summary>
        public void UpdateImageMetadata(string patientId, string imageId, string newTag, string newDescription)
        {
            var profile = LoadPatient(patientId);
            if (profile == null) return;

            var image = profile.AssociatedImages.FirstOrDefault(i => i.ImageId == imageId);
            if (image == null) return;

            image.Tag = newTag?.Trim() ?? "";
            image.Description = newDescription?.Trim() ?? "";
            profile.LastModifiedDate = DateTime.UtcNow;
            SavePatient(profile);
        }

        /// <summary>
        /// Returns the full path to a patient image file on disk.
        /// </summary>
        public string GetImageFullPath(string patientId, string imageFileName)
            => Path.Combine(GetPatientImageDir(patientId), imageFileName);

        private string GetPatientImageDir(string patientId)
            => Path.Combine(_storageDir, "images", patientId);

        private string GetPatientFilePath(string patientId)
            => Path.Combine(_storageDir, $"{patientId}.enc");

        // ── AES-256-CBC Encryption ──────────────────────────────────────────────
        // Format: [16-byte IV] [ciphertext with PKCS7 padding]

        private byte[] Encrypt(byte[] plaintext)
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encryptionKey;
            aes.GenerateIV(); // Random IV per encryption

            using var encryptor = aes.CreateEncryptor();
            byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

            // Prepend IV to ciphertext
            byte[] result = new byte[IvSize + ciphertext.Length];
            Array.Copy(aes.IV, 0, result, 0, IvSize);
            Array.Copy(ciphertext, 0, result, IvSize, ciphertext.Length);
            return result;
        }

        private byte[] Decrypt(byte[] data)
        {
            if (data.Length < IvSize + 1)
                throw new CryptographicException("Encrypted data is too short.");

            byte[] iv = new byte[IvSize];
            Array.Copy(data, 0, iv, 0, IvSize);

            byte[] ciphertext = new byte[data.Length - IvSize];
            Array.Copy(data, IvSize, ciphertext, 0, ciphertext.Length);

            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _encryptionKey;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }
    }
}
