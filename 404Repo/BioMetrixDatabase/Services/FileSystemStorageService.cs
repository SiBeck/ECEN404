using Microsoft.Extensions.Options;

namespace MedicalImagingAPI.Services
{
    public class StorageSettings
    {
        public string Type { get; set; }
        public string BasePath { get; set; }
        public int MaxFileSizeMB { get; set; }
        public List<string> AllowedExtensions { get; set; }
    }

    public class FileSystemStorageService : IStorageService
    {
        private readonly StorageSettings _settings;
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<FileSystemStorageService> _logger;

        public FileSystemStorageService(
            IOptions<StorageSettings> settings,
            IEncryptionService encryptionService,
            ILogger<FileSystemStorageService> logger)
        {
            _settings = settings.Value;
            _encryptionService = encryptionService;
            _logger = logger;

            // Ensure base directory exists
            if (!Directory.Exists(_settings.BasePath))
            {
                Directory.CreateDirectory(_settings.BasePath);
            }
        }

        public async Task<string> SaveFileAsync(Stream fileStream, string fileName, string patientId, string studyId)
        {
            try
            {

                _logger.LogInformation($"SaveFileAsync called - File: {fileName}, Patient: {patientId}, Study: {studyId}");
                _logger.LogInformation($"Stream - CanRead: {fileStream.CanRead}, CanSeek: {fileStream.CanSeek}, Position: {fileStream.Position}, Length: {(fileStream.CanSeek ? fileStream.Length : -1)}");

                // Reset stream position
                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                    _logger.LogInformation("Stream position reset to 0");
                }

                // CRITICAL: Reset stream position before reading
                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                }

                // Create directory structure: BasePath/patients/patientId/studies/studyId/
                var patientDir = Path.Combine(_settings.BasePath, "patients", patientId, "studies", studyId);
                Directory.CreateDirectory(patientDir);

                // Sanitize filename
                var safeFileName = Path.GetFileName(fileName);
                var filePath = Path.Combine(patientDir, safeFileName);

                // Read file into memory
                using var ms = new MemoryStream();
                await fileStream.CopyToAsync(ms);
                var fileBytes = ms.ToArray();

                // Encrypt the file data
                var encryptedData = _encryptionService.EncryptBytes(fileBytes);

                // Save encrypted data to disk
                await File.WriteAllBytesAsync(filePath, encryptedData);

                _logger.LogInformation($"File saved: {filePath}");

                // Return relative path for database storage
                return Path.Combine("patients", patientId, "studies", studyId, safeFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving file: {fileName}");
                throw;
            }
        }

        public async Task<Stream> GetFileAsync(string relativePath)
        {
            try
            {
                var fullPath = Path.Combine(_settings.BasePath, relativePath);

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

        public async Task<bool> DeleteFileAsync(string relativePath)
        {
            try
            {
                var fullPath = Path.Combine(_settings.BasePath, relativePath);

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation($"File deleted: {fullPath}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file: {relativePath}");
                return false;
            }
        }

        public Task<bool> FileExistsAsync(string relativePath)
        {
            var fullPath = Path.Combine(_settings.BasePath, relativePath);
            return Task.FromResult(File.Exists(fullPath));
        }

        public Task<long> GetFileSizeAsync(string relativePath)
        {
            var fullPath = Path.Combine(_settings.BasePath, relativePath);
            var fileInfo = new FileInfo(fullPath);
            return Task.FromResult(fileInfo.Length);
        }
    }
}