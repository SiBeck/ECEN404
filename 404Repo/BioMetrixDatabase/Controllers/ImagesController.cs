using MedicalImagingAPI.Attributes;
using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using MedicalImagingAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using FellowOakDicom;

namespace MedicalImagingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ImagesController : ControllerBase
    {
        private readonly IStorageService _storageService;
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ILogger<ImagesController> _logger;

        public ImagesController(
            IStorageService storageService,
            ApplicationDbContext context,
            IAuditService auditService,
            ILogger<ImagesController> logger)
        {
            _storageService = storageService;
            _context = context;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpPost("upload")]
        [Audit("UPLOAD_IMAGE", "DicomImage")]
        public async Task<IActionResult> UploadDicom(IFormFile file, [FromForm] string patientId)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            if (!file.FileName.EndsWith(".dcm", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Only DICOM files (.dcm) are allowed");

            var userId = GetCurrentUserId();

            try
            {
                // Parse DICOM file to extract metadata
                DicomFile dicomFile;
                using (var stream = file.OpenReadStream())
                {
                    dicomFile = await DicomFile.OpenAsync(stream);
                }

                var dataset = dicomFile.Dataset;
                var studyId = dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, Guid.NewGuid().ToString());
                var seriesId = dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, Guid.NewGuid().ToString());
                var instanceId = dataset.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, Guid.NewGuid().ToString());

                // Calculate file hash
                string fileHash;
                using (var stream = file.OpenReadStream())
                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = await sha256.ComputeHashAsync(stream);
                    fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }

                // Save file to storage
                string filePath;
                using (var stream = file.OpenReadStream())
                {
                    filePath = await _storageService.SaveFileAsync(stream, file.FileName, patientId, studyId);
                }

                // Create database record
                var dicomImage = new DicomImage
                {
                    Id = Guid.NewGuid(),
                    PatientId = patientId,
                    StudyId = studyId,
                    SeriesId = seriesId,
                    InstanceId = instanceId,
                    FilePath = filePath,
                    FileName = file.FileName,
                    FileSizeBytes = file.Length,
                    FileHash = fileHash,
                    UploadedAt = DateTime.UtcNow,
                    UploadedBy = userId,
                    FrameCount = dataset.GetSingleValueOrDefault(DicomTag.NumberOfFrames, 1),
                    Modality = dataset.GetSingleValueOrDefault(DicomTag.Modality, ""),
                    StudyDate = dataset.GetSingleValueOrDefault<DateTime?>(DicomTag.StudyDate, null),
                    StudyDescription = dataset.GetSingleValueOrDefault(DicomTag.StudyDescription, ""),
                    SeriesDescription = dataset.GetSingleValueOrDefault(DicomTag.SeriesDescription, ""),
                    Width = dataset.GetSingleValueOrDefault<int?>(DicomTag.Columns, null),
                    Height = dataset.GetSingleValueOrDefault<int?>(DicomTag.Rows, null),
                    IsDeleted = false
                };

                _context.DicomImages.Add(dicomImage);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(userId, "UPLOAD_IMAGE", "DicomImage", dicomImage.Id.ToString(), true);

                return Ok(new
                {
                    id = dicomImage.Id,
                    fileName = dicomImage.FileName,
                    fileSize = dicomImage.FileSizeBytes,
                    frameCount = dicomImage.FrameCount,
                    uploadedAt = dicomImage.UploadedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading DICOM file");
                await _auditService.LogAsync(userId, "UPLOAD_IMAGE", "DicomImage", "N/A", false, ex.Message);
                return StatusCode(500, "Error uploading file");
            }
        }

        [HttpGet("{id}")]
        [Audit("VIEW_IMAGE", "DicomImage")]
        public async Task<IActionResult> GetDicom(Guid id)
        {
            var userId = GetCurrentUserId();

            try
            {
                var dicomImage = await _context.DicomImages.FindAsync(id);

                if (dicomImage == null || dicomImage.IsDeleted)
                    return NotFound();

                var fileStream = await _storageService.GetFileAsync(dicomImage.FilePath);

                await _auditService.LogAsync(userId, "VIEW_IMAGE", "DicomImage", id.ToString(), true);

                return File(fileStream, "application/dicom", dicomImage.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving DICOM file: {id}");
                await _auditService.LogAsync(userId, "VIEW_IMAGE", "DicomImage", id.ToString(), false, ex.Message);
                return StatusCode(500, "Error retrieving file");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ListImages([FromQuery] string patientId = null)
        {
            var query = _context.DicomImages.Where(d => !d.IsDeleted);

            if (!string.IsNullOrEmpty(patientId))
            {
                query = query.Where(d => d.PatientId == patientId);
            }

            var images = await query
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new
                {
                    d.Id,
                    d.PatientId,
                    d.FileName,
                    d.FileSizeBytes,
                    d.FrameCount,
                    d.Modality,
                    d.StudyDate,
                    d.UploadedAt
                })
                .ToListAsync();

            return Ok(images);
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "RequirePhysician")]
        [Audit("DELETE_IMAGE", "DicomImage")]
        public async Task<IActionResult> DeleteDicom(Guid id)
        {
            var userId = GetCurrentUserId();

            try
            {
                var dicomImage = await _context.DicomImages.FindAsync(id);

                if (dicomImage == null)
                    return NotFound();

                // Soft delete
                dicomImage.IsDeleted = true;
                dicomImage.DeletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Optionally delete physical file
                // await _storageService.DeleteFileAsync(dicomImage.FilePath);

                await _auditService.LogAsync(userId, "DELETE_IMAGE", "DicomImage", id.ToString(), true);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting DICOM file: {id}");
                await _auditService.LogAsync(userId, "DELETE_IMAGE", "DicomImage", id.ToString(), false, ex.Message);
                return StatusCode(500, "Error deleting file");
            }
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.Parse(userIdClaim);
        }
    }
}