using Microsoft.EntityFrameworkCore;
using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using System.Security.Cryptography;

namespace MedicalImagingAPI.Services
{
    public interface IPatientFileService
    {
        Task<PatientFile> UploadFileAsync(Guid patientId, Stream fileStream, string fileName, string fileType, string category, string description, Guid uploadedBy);
        Task<PatientFile> GetFileByIdAsync(Guid id);
        Task<List<PatientFile>> GetFilesByPatientIdAsync(Guid patientId);
        Task<Stream> DownloadFileAsync(Guid fileId);
        Task<bool> DeleteFileAsync(Guid id);
    }

    public class PatientFileService : IPatientFileService
    {
        private readonly ApplicationDbContext _context;
        private readonly IStorageService _storageService;
        private readonly IAuditService _auditService;

        public PatientFileService(
            ApplicationDbContext context,
            IStorageService storageService,
            IAuditService auditService)
        {
            _context = context;
            _storageService = storageService;
            _auditService = auditService;
        }

        public async Task<PatientFile> UploadFileAsync(
            Guid patientId,
            Stream fileStream,
            string fileName,
            string fileType,
            string category,
            string description,
            Guid uploadedBy)
        {
            var patient = await _context.Patients.FindAsync(patientId);
            if (patient == null)
                throw new Exception("Patient not found");

            // Calculate file size
            var fileBytes = new byte[fileStream.Length];
            await fileStream.ReadAsync(fileBytes, 0, fileBytes.Length);
            fileStream.Position = 0;

            // Save to storage
            var filePath = await _storageService.SaveFileAsync(
                fileStream,
                fileName,
                patient.PatientId,
                Guid.NewGuid().ToString() // Use unique ID as "study"
            );

            // Create database record
            var patientFile = new PatientFile
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                FileName = fileName,
                FileType = fileType,
                FileSize = fileBytes.Length,
                Description = description,
                Category = category,
                FilePath = filePath,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = uploadedBy.ToString()
            };

            _context.PatientFiles.Add(patientFile);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                uploadedBy,
                "UPLOAD_PATIENT_FILE",
                "PatientFile",
                patientFile.Id.ToString(),
                true
            );

            return patientFile;
        }

        public async Task<PatientFile> GetFileByIdAsync(Guid id)
        {
            return await _context.PatientFiles
                .Include(f => f.Patient)
                .FirstOrDefaultAsync(f => f.Id == id);
        }

        public async Task<List<PatientFile>> GetFilesByPatientIdAsync(Guid patientId)
        {
            return await _context.PatientFiles
                .Where(f => f.PatientId == patientId)
                .OrderByDescending(f => f.UploadedAt)
                .ToListAsync();
        }

        public async Task<Stream> DownloadFileAsync(Guid fileId)
        {
            var file = await GetFileByIdAsync(fileId);
            if (file == null)
                throw new Exception("File not found");

            return await _storageService.GetFileAsync(file.FilePath);
        }

        public async Task<bool> DeleteFileAsync(Guid id)
        {
            var file = await _context.PatientFiles.FindAsync(id);
            if (file == null)
                return false;

            // Hard delete - remove from storage
            await _storageService.DeleteFileAsync(file.FilePath);

            // Remove from database
            _context.PatientFiles.Remove(file);
            await _context.SaveChangesAsync();

            return true;
        }
    }
}