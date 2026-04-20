using PatientPortal.Models;
using System.Threading.Tasks;

namespace PatientPortal.Services
{
    public enum RegistrationError
    {
        None,
        InvalidPatientIdFormat,
        PatientNotFound,
        AlreadyRegistered,
        PasswordPolicyViolation
    }

    public class RegistrationResult
    {
        public bool Success { get; set; }
        public RegistrationError Error { get; set; } = RegistrationError.None;
        public string? ErrorMessage { get; set; }
        public Patient? Patient { get; set; }
    }

    public interface IPatientAuthService
    {
        Task<Patient?> AuthenticateAsync(string patientId, string password);
        Task<RegistrationResult> RegisterAsync(RegisterViewModel model);
        Task<bool> ChangePasswordAsync(Guid patientId, string currentPassword, string newPassword);
        Task<bool> ChangePasswordAsync(int patientId, string currentPassword, string newPassword);
        Task<Patient?> GetPatientByIdAsync(Guid id);
        Task<Patient?> GetPatientByPatientIdAsync(string patientId);
    }
}
