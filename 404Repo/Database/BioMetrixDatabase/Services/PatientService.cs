using Microsoft.EntityFrameworkCore;
using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;

namespace MedicalImagingAPI.Services
{
    public interface IPatientService
    {
        Task<Patient> CreatePatientAsync(Patient patient);
        Task<Patient> GetPatientByIdAsync(Guid id);
        Task<Patient> GetPatientByPatientIdAsync(string patientId);
        Task<List<Patient>> GetAllPatientsAsync();
        Task<Patient> UpdatePatientAsync(Patient patient);
        Task<bool> DeletePatientAsync(Guid id);
        Task<List<Patient>> SearchPatientsAsync(string searchTerm);
    }

    public class PatientService : IPatientService
    {
        private readonly ApplicationDbContext _context;

        public PatientService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Patient> CreatePatientAsync(Patient patient)
        {
            patient.Id = Guid.NewGuid();
            patient.CreatedAt = DateTime.UtcNow;
            patient.IsActive = true;

            _context.Patients.Add(patient);
            await _context.SaveChangesAsync();

            return patient;
        }

        public async Task<Patient> GetPatientByIdAsync(Guid id)
        {
            return await _context.Patients
                .Include(p => p.DicomImages)
                .Include(p => p.PatientFiles)
                .FirstOrDefaultAsync(p => p.Id == id && p.IsActive);
        }

        public async Task<Patient> GetPatientByPatientIdAsync(string patientId)
        {
            return await _context.Patients
                .Include(p => p.DicomImages)
                .Include(p => p.PatientFiles)
                .FirstOrDefaultAsync(p => p.PatientId == patientId && p.IsActive);
        }

        public async Task<List<Patient>> GetAllPatientsAsync()
        {
            return await _context.Patients
                .Where(p => p.IsActive)
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync();
        }

        public async Task<Patient> UpdatePatientAsync(Patient patient)
        {
            var existing = await _context.Patients.FindAsync(patient.Id);
            if (existing == null)
                return null;

            existing.FirstName = patient.FirstName;
            existing.LastName = patient.LastName;
            existing.DateOfBirth = patient.DateOfBirth;
            existing.Gender = patient.Gender;
            existing.Email = patient.Email;
            existing.Phone = patient.Phone;
            existing.Address = patient.Address;
            existing.City = patient.City;
            existing.State = patient.State;
            existing.ZipCode = patient.ZipCode;
            existing.EmergencyContact = patient.EmergencyContact;
            existing.EmergencyPhone = patient.EmergencyPhone;
            existing.InsuranceProvider = patient.InsuranceProvider;
            existing.InsuranceNumber = patient.InsuranceNumber;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeletePatientAsync(Guid id)
        {
            var patient = await _context.Patients.FindAsync(id);
            if (patient == null)
                return false;

            patient.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<Patient>> SearchPatientsAsync(string searchTerm)
        {
            searchTerm = searchTerm.ToLower();

            return await _context.Patients
                .Where(p => p.IsActive && (
                    p.PatientId.ToLower().Contains(searchTerm) ||
                    p.FirstName.ToLower().Contains(searchTerm) ||
                    p.LastName.ToLower().Contains(searchTerm) ||
                    p.Email.ToLower().Contains(searchTerm)
                ))
                .OrderBy(p => p.LastName)
                .ToListAsync();
        }
    }
}