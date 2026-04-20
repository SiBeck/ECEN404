using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace PatientPortal.Services
{
    public class FileSystemStorageService : IStorageService
    {
        private readonly string _basePath;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<FileSystemStorageService> _logger;

        public FileSystemStorageService(
            IConfiguration configuration,
            IEncryptionService encryptionService,
            ILogger<FileSystemStorageService> logger)
        {
            _basePath = configuration["Storage:BasePath"] ?? "C:\\MedicalImagingStorage";
            _encryptionService = encryptionService;
            _logger = logger;

            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }
        }

        public async Task<Stream> GetFileAsync(string relativePath)
        {
            try
            {
                var fullPath = Path.Combine(_basePath, relativePath);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {relativePath}");
                }

                // Read encrypted file
                var encryptedData = await File.ReadAllBytesAsync(fullPath);

                // Decrypt the data
                var decryptedData = _encryptionService.DecryptBytes(encryptedData);

                // Return as stream
                return new MemoryStream(decryptedData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving file: {relativePath}");
                throw;
            }
        }

        public Task<bool> FileExistsAsync(string relativePath)
        {
            var fullPath = Path.Combine(_basePath, relativePath);
            return Task.FromResult(File.Exists(fullPath));
        }
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration)
        {
            _key = Convert.FromBase64String(configuration["Encryption:Key"] ?? "");
            _iv = Convert.FromBase64String(configuration["Encryption:IV"] ?? "");
        }

        public byte[] EncryptBytes(byte[] data)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);

            csEncrypt.Write(data, 0, data.Length);
            csEncrypt.FlushFinalBlock();

            return msEncrypt.ToArray();
        }

        public byte[] DecryptBytes(byte[] encryptedData)
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = _iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var msDecrypt = new MemoryStream(encryptedData);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var msPlain = new MemoryStream();

            csDecrypt.CopyTo(msPlain);
            return msPlain.ToArray();
        }
    }
}
