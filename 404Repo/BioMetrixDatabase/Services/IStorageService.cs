namespace MedicalImagingAPI.Services
{
    public interface IStorageService
    {
        Task<string> SaveFileAsync(Stream fileStream, string fileName, string patientId, string studyId);
        Task<Stream> GetFileAsync(string filePath);
        Task<bool> DeleteFileAsync(string filePath);
        Task<bool> FileExistsAsync(string filePath);
        Task<long> GetFileSizeAsync(string filePath);
    }
}