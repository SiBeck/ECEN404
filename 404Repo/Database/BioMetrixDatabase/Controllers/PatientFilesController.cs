using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using MedicalImagingAPI.Services;
using System.Security.Claims;

namespace MedicalImagingAPI.Controllers
{
    [ApiController]
    [Route("api/patients/{patientId}/files")]
    [Authorize]
    public class PatientFilesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IStorageService _storageService;
        private readonly IAuditService _auditService;
        private readonly ILogger<PatientFilesController> _logger;

        public PatientFilesController(
            ApplicationDbContext context,
            IStorageService storageService,
            IAuditService auditService,
            ILogger<PatientFilesController> logger)
        {
            _context = context;
            _storageService = storageService;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpPost]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(524288000)] // 500 MB
        [Authorize(Policy = "RequireMedicalStaff")]
        public async Task<IActionResult> UploadFile(
            Guid patientId,
            IFormFile file,
            [FromForm] string fileType,
            [FromForm] string category,
            [FromForm] string? description)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest("No file uploaded");

                _logger.LogInformation($"Receiving file upload: {file.FileName}, Size: {file.Length} bytes, Patient: {patientId}");

                // Verify patient exists
                var patient = await _context.Patients.FindAsync(patientId);
                if (patient == null)
                    return NotFound($"Patient not found: {patientId}");

                // Generate a study ID for this upload
                var studyId = Guid.NewGuid().ToString();

                // Save file to storage
                string relativePath;
                using (var stream = file.OpenReadStream())
                {
                    // Reset stream position if possible
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    relativePath = await _storageService.SaveFileAsync(
                        stream,
                        file.FileName,
                        patientId.ToString(),
                        studyId
                    );
                }

                _logger.LogInformation($"File saved to storage: {relativePath}");

                // Create database record
                var patientFile = new PatientFile
                {
                    Id = Guid.NewGuid(),
                    PatientId = patientId,
                    FileName = file.FileName,
                    FilePath = relativePath,
                    FileType = fileType,
                    Category = category,
                    Description = description,
                    FileSize = file.Length,
                    UploadedAt = DateTime.UtcNow,
                    UploadedBy = GetCurrentProviderId()
                };

                _context.PatientFiles.Add(patientFile);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Database record created for file: {patientFile.Id}");

                // Log audit
                await _auditService.LogAsync(
                    GetCurrentUserId(),
                    "UPLOAD_FILE",
                    "PatientFile",
                    patientFile.Id.ToString(),
                    true,
                    $"Uploaded {file.FileName} for patient {patientId}"
                );

                return Ok(new
                {
                    patientFile.Id,
                    patientFile.PatientId,
                    patientFile.FileName,
                    patientFile.FileType,
                    patientFile.UploadedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error uploading file for patient {patientId}");

                await _auditService.LogAsync(
                    GetCurrentUserId(),
                    "UPLOAD_FILE",
                    "PatientFile",
                    patientId.ToString(),
                    false,
                    ex.Message
                );

                return StatusCode(500, new { error = $"Error uploading file: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPatientFiles(Guid patientId)
        {
            try
            {
                var files = _context.PatientFiles
                    .Where(f => f.PatientId == patientId)
                    .OrderByDescending(f => f.UploadedAt)
                    .ToList();

                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving files for patient {patientId}");
                return StatusCode(500, "Error retrieving files");
            }
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> DownloadFile(Guid patientId, Guid fileId)
        {
            try
            {
                var file = await _context.PatientFiles.FindAsync(fileId);
                if (file == null || file.PatientId != patientId)
                    return NotFound();

                var fileStream = await _storageService.GetFileAsync(file.FilePath);

                return File(fileStream, "application/octet-stream", file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file {fileId}");
                return StatusCode(500, "Error downloading file");
            }
        }

        [HttpDelete("{fileId}")]
        [Authorize(Policy = "RequirePhysician")]
        public async Task<IActionResult> DeleteFile(Guid patientId, Guid fileId)
        {
            try
            {
                var file = await _context.PatientFiles.FindAsync(fileId);
                if (file == null || file.PatientId != patientId)
                    return NotFound();

                // Delete from storage
                await _storageService.DeleteFileAsync(file.FilePath);

                // Delete from database
                _context.PatientFiles.Remove(file);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    GetCurrentUserId(),
                    "DELETE_FILE",
                    "PatientFile",
                    fileId.ToString(),
                    true
                );

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting file {fileId}");
                return StatusCode(500, "Error deleting file");
            }
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
        }

        private string GetCurrentProviderId()
        {
            return User.FindFirst("providerId")?.Value ?? "Unknown";
        }
    }
}