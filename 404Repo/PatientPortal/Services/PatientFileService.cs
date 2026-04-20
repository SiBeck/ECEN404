using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PatientPortal.Data;
using PatientPortal.Models;

namespace PatientPortal.Services
{
    public class PatientFileService : IPatientFileService
    {
        private readonly ApplicationDbContext _context;
        private readonly IStorageService _storageService;
        private readonly IBioMetrixDatabaseService? _bioMetrix;

        public PatientFileService(
            ApplicationDbContext context,
            IStorageService storageService,
            IBioMetrixDatabaseService? bioMetrix = null)
        {
            _context = context;
            _storageService = storageService;
            _bioMetrix = bioMetrix;
        }

        public async Task<List<PatientFile>> GetPatientFilesAsync(int patientId)
        {
            // If BioMetrixDatabase integration is available, prefer it as the
            // authoritative file store. Fall back to local DB if unavailable.
            if (_bioMetrix != null)
            {
                try
                {
                    var remoteFiles = await _bioMetrix.GetPatientFilesAsync(patientId.ToString());
                    if (remoteFiles.Count > 0 || await HasNoLocalFilesAsync(patientId))
                        return remoteFiles;
                }
                catch (Exception)
                {
                    // BioMetrixDatabase unreachable — fall through to local storage
                }
            }

            return await _context.PatientFiles
                .Where(f => f.PatientId == patientId)
                .OrderByDescending(f => f.UploadedAt)
                .ToListAsync();
        }

        public async Task<PatientFile?> GetFileByIdAsync(Guid fileId, int patientId)
        {
            if (_bioMetrix != null)
            {
                try
                {
                    return await _bioMetrix.GetFileMetadataAsync(fileId, patientId.ToString());
                }
                catch (Exception) { }
            }

            return await _context.PatientFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.PatientId == patientId);
        }

        public async Task<Stream?> GetFileStreamAsync(PatientFile file)
        {
            // Files originating from BioMetrixDatabase carry a virtual path
            // in the form "biometrix://{patientGuid}/{fileGuid}".
            if (_bioMetrix != null && file.FilePath.StartsWith("biometrix://",
                StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var parts = file.FilePath["biometrix://".Length..].Split('/');
                    if (parts.Length == 2
                        && Guid.TryParse(parts[0], out var patientGuid)
                        && Guid.TryParse(parts[1], out var fileGuid))
                    {
                        return await ((BioMetrixDatabaseService)_bioMetrix)
                            .DownloadFileAsync(patientGuid, fileGuid);
                    }
                }
                catch (Exception) { }
            }

            // Local fallback
            try
            {
                return await _storageService.GetFileAsync(file.FilePath);
            }
            catch
            {
                return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<bool> HasNoLocalFilesAsync(int patientId)
            => !await _context.PatientFiles.AnyAsync(f => f.PatientId == patientId);
    }
}
