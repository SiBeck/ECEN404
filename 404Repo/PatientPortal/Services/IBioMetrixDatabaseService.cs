using PatientPortal.Models;

namespace PatientPortal.Services
{
    /// <summary>
    /// Bridge from PatientPortal to the BioMetrixDatabase REST API.
    /// PatientPortal authenticates patients; this service lets the portal
    /// read and write files on behalf of authenticated patients by forwarding
    /// requests to BioMetrixDatabase using a privileged service-account token.
    /// </summary>
    public interface IBioMetrixDatabaseService
    {
        /// <summary>
        /// Returns all files stored in BioMetrixDatabase for a patient identified
        /// by their medical record number (MRN).
        /// </summary>
        Task<List<PatientFile>> GetPatientFilesAsync(string mrn);

        /// <summary>
        /// Fetches metadata for a single file without downloading its contents.
        /// Returns null if the file is not found or does not belong to the patient.
        /// </summary>
        Task<PatientFile?> GetFileMetadataAsync(Guid fileId, string mrn);

        /// <summary>
        /// Streams a file's bytes from BioMetrixDatabase storage.
        /// Returns null if not found.
        /// </summary>
        Task<Stream?> DownloadFileAsync(Guid fileId);

        /// <summary>
        /// Uploads a file to BioMetrixDatabase on behalf of the patient.
        /// Returns the newly created file metadata.
        /// </summary>
        Task<PatientFile?> UploadFileAsync(
            string mrn,
            Stream fileStream,
            string fileName,
            string fileType,
            string category,
            string description);

        /// <summary>
        /// Deletes a file from BioMetrixDatabase. Returns true on success.
        /// </summary>
        Task<bool> DeleteFileAsync(Guid fileId);
    }
}
