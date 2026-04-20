using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientPortal.Services;
using System.Security.Claims;

namespace PatientPortal.Controllers
{
    [Authorize]
    public class FilesController : Controller
    {
        private readonly IPatientFileService _fileService;
        private readonly ILogger<FilesController> _logger;

        public FilesController(IPatientFileService fileService, ILogger<FilesController> logger)
        {
            _fileService = fileService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var patientIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(patientIdClaim) || !int.TryParse(patientIdClaim, out var patientId))
            {
                return RedirectToAction("Login", "Account");
            }

            var files = await _fileService.GetPatientFilesAsync(patientId);
            return View(files);
        }

        public async Task<IActionResult> Download(Guid id)
        {
            var patientIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(patientIdClaim) || !int.TryParse(patientIdClaim, out var patientId))
            {
                return RedirectToAction("Login", "Account");
            }

            var file = await _fileService.GetFileByIdAsync(id, patientId);
            if (file == null)
            {
                return NotFound();
            }

            try
            {
                var fileStream = await _fileService.GetFileStreamAsync(file);
                if (fileStream == null)
                {
                    TempData["ErrorMessage"] = "File not found or could not be accessed.";
                    return RedirectToAction(nameof(Index));
                }

                var contentType = GetContentType(file.FileName);
                return File(fileStream, contentType, file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error downloading file {id}");
                TempData["ErrorMessage"] = "An error occurred while downloading the file.";
                return RedirectToAction(nameof(Index));
            }
        }

        public async Task<IActionResult> View(Guid id)
        {
            var patientIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(patientIdClaim) || !int.TryParse(patientIdClaim, out var patientId))
            {
                return RedirectToAction("Login", "Account");
            }

            var file = await _fileService.GetFileByIdAsync(id, patientId);
            if (file == null)
            {
                return NotFound();
            }

            ViewBag.File = file;
            return View(file);
        }

        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".dcm" or ".dicom" => "application/dicom",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }
}
