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

        public PatientFileService(ApplicationDbContext context, IStorageService storageService)
        {
            _context = context;
            _storageService = storageService;
        }

        public async Task<List<PatientFile>> GetPatientFilesAsync(int patientId)
        {
            return await _context.PatientFiles
                .Where(f => f.PatientId == patientId)
                .OrderByDescending(f => f.UploadedAt)
                .ToListAsync();
        }

        public async Task<PatientFile?> GetFileByIdAsync(Guid fileId, int patientId)
        {
            return await _context.PatientFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.PatientId == patientId);
        }

        public async Task<Stream?> GetFileStreamAsync(PatientFile file)
        {
            try
            {
                return await _storageService.GetFileAsync(file.FilePath);
            }
            catch
            {
                return null;
            }
        }
    }
}
