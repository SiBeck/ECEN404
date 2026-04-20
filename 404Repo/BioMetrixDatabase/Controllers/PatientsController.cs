using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MedicalImagingAPI.Database.Entities;
using MedicalImagingAPI.Services;
using System.Security.Claims;

namespace MedicalImagingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PatientsController : ControllerBase
    {
        private readonly IPatientService _patientService;
        private readonly IAuditService _auditService;

        public PatientsController(IPatientService patientService, IAuditService auditService)
        {
            _patientService = patientService;
            _auditService = auditService;
        }

        [HttpPost]
        [Authorize(Policy = "RequireMedicalStaff")]
        public async Task<IActionResult> CreatePatient([FromBody] CreatePatientRequest request)
        {
            var patient = new Patient
            {
                PatientId = request.PatientId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender,
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                City = request.City,
                State = request.State,
                ZipCode = request.ZipCode,
                EmergencyContact = request.EmergencyContact,
                EmergencyPhone = request.EmergencyPhone,
                InsuranceProvider = request.InsuranceProvider,
                InsuranceNumber = request.InsuranceNumber
            };

            var createdPatient = await _patientService.CreatePatientAsync(patient);

            await _auditService.LogAsync(
                GetCurrentUserId(),
                "CREATE_PATIENT",
                "Patient",
                createdPatient.Id.ToString(),
                true
            );

            return Ok(createdPatient);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllPatients()
        {
            var patients = await _patientService.GetAllPatientsAsync();
            return Ok(patients);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetPatient(Guid id)
        {
            var patient = await _patientService.GetPatientByIdAsync(id);
            if (patient == null)
                return NotFound();

            return Ok(patient);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchPatients([FromQuery] string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest("Search term is required");

            var patients = await _patientService.SearchPatientsAsync(q);
            return Ok(patients);
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "RequireMedicalStaff")]
        public async Task<IActionResult> UpdatePatient(Guid id, [FromBody] UpdatePatientRequest request)
        {
            var patient = new Patient
            {
                Id = id,
                FirstName = request.FirstName,
                LastName = request.LastName,
                DateOfBirth = request.DateOfBirth,
                Gender = request.Gender,
                Email = request.Email,
                Phone = request.Phone,
                Address = request.Address,
                City = request.City,
                State = request.State,
                ZipCode = request.ZipCode,
                EmergencyContact = request.EmergencyContact,
                EmergencyPhone = request.EmergencyPhone,
                InsuranceProvider = request.InsuranceProvider,
                InsuranceNumber = request.InsuranceNumber
            };

            var updatedPatient = await _patientService.UpdatePatientAsync(patient);
            if (updatedPatient == null)
                return NotFound();

            await _auditService.LogAsync(
                GetCurrentUserId(),
                "UPDATE_PATIENT",
                "Patient",
                id.ToString(),
                true
            );

            return Ok(updatedPatient);
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "RequirePhysician")]
        public async Task<IActionResult> DeletePatient(Guid id)
        {
            var result = await _patientService.DeletePatientAsync(id);
            if (!result)
                return NotFound();

            await _auditService.LogAsync(
                GetCurrentUserId(),
                "DELETE_PATIENT",
                "Patient",
                id.ToString(),
                true
            );

            return NoContent();
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.Parse(userIdClaim);
        }
    }

    public class CreatePatientRequest
    {
        public string PatientId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public string? InsuranceProvider { get; set; }
        public string? InsuranceNumber { get; set; }
    }

    public class UpdatePatientRequest
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string EmergencyContact { get; set; }
        public string EmergencyPhone { get; set; }
        public string InsuranceProvider { get; set; }
        public string InsuranceNumber { get; set; }
    }
}