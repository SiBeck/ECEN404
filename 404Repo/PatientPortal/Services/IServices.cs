using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PatientPortal.Models;

namespace PatientPortal.Services
{
    public interface IPatientFileService
    {
        Task<List<PatientFile>> GetPatientFilesAsync(int patientId);
        Task<PatientFile?> GetFileByIdAsync(Guid fileId, int patientId);
        Task<Stream?> GetFileStreamAsync(PatientFile file);
    }

    public interface IStorageService
    {
        Task<Stream> GetFileAsync(string relativePath);
        Task<bool> FileExistsAsync(string relativePath);
    }

    public interface IEncryptionService
    {
        byte[] EncryptBytes(byte[] data);
        byte[] DecryptBytes(byte[] encryptedData);
    }
}
