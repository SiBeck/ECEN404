using Microsoft.EntityFrameworkCore;
using PatientPortal.Data;
using PatientPortal.Models;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PatientPortal.Services
{
    public class PatientAuthService : IPatientAuthService
    {
        private readonly ApplicationDbContext _context;

        public PatientAuthService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Patient?> AuthenticateAsync(string patientIdString, string password)
        {
            if (!int.TryParse(patientIdString, out int patientId))
                return null;

            // Require a password for sign-on.
            if (string.IsNullOrWhiteSpace(password))
                return null;

            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == patientId && p.PasswordHash != null);

            if (patient == null)
                return null;

            return BCrypt.Net.BCrypt.Verify(password, patient.PasswordHash) ? patient : null;
        }

        public async Task<RegistrationResult> RegisterAsync(RegisterViewModel model)
        {
            // Validate numeric MRN
            if (!int.TryParse(model.PatientId, out int patientId))
            {
                return new RegistrationResult
                {
                    Success = false,
                    Error = RegistrationError.InvalidPatientIdFormat,
                    ErrorMessage = "Patient ID must be a numeric medical record number."
                };
            }

            // Validate password policy server-side (defense in depth)
            var passwordPattern = @"^(?=.{6,}$)(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).*$";
            if (string.IsNullOrWhiteSpace(model.Password) || !Regex.IsMatch(model.Password, passwordPattern))
            {
                return new RegistrationResult
                {
                    Success = false,
                    Error = RegistrationError.PasswordPolicyViolation,
                    ErrorMessage = "Password must be at least 6 characters and include at least one uppercase letter, one number, and one special character."
                };
            }

            // Check patient exists (created by medical staff)
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == patientId);

            if (patient == null)
            {
                return new RegistrationResult
                {
                    Success = false,
                    Error = RegistrationError.PatientNotFound,
                    ErrorMessage = "Patient ID not found in the system. Contact medical staff to create the record first."
                };
            }

            // Check already registered
            if (patient.PasswordHash != null)
            {
                return new RegistrationResult
                {
                    Success = false,
                    Error = RegistrationError.AlreadyRegistered,
                    ErrorMessage = "A portal account already exists for this patient ID."
                };
            }

            // Register: set password and optional contact updates
            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
            patient.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(model.Email))
                patient.Email = model.Email;
            if (!string.IsNullOrEmpty(model.Phone))
                patient.Phone = model.Phone;

            await _context.SaveChangesAsync();

            return new RegistrationResult
            {
                Success = true,
                Error = RegistrationError.None,
                Patient = patient
            };
        }

        // Existing integer-based change password kept for compatibility
        public async Task<bool> ChangePasswordAsync(int patientId, string currentPassword, string newPassword)
        {
            var patient = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == patientId);

            if (patient == null || patient.PasswordHash == null)
                return false;

            if (!BCrypt.Net.BCrypt.Verify(currentPassword, patient.PasswordHash))
                return false;

            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            patient.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // Interface-required Guid-based ChangePasswordAsync
        public async Task<bool> ChangePasswordAsync(Guid patientId, string currentPassword, string newPassword)
        {
            var patient = await GetPatientByIdAsync(patientId);
            if (patient == null || patient.PasswordHash == null) return false;
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, patient.PasswordHash)) return false;
            patient.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            patient.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        // Interface-required: get patient by database/primary key Guid (best-effort)
        public async Task<Patient?> GetPatientByIdAsync(Guid id)
        {
            if (int.TryParse(id.ToString(), out int intId))
                return await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == intId);
            return null;
        }

        // Interface-required: get patient by patient identifier (string)
        public async Task<Patient?> GetPatientByPatientIdAsync(string patientId)
        {
            if (int.TryParse(patientId, out int intPatientId))
                return await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == intPatientId);
            return await _context.Patients.FirstOrDefaultAsync(p => p.PatientId.ToString() == patientId);
        }
    }
}