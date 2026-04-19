using BioMetrixDatabase.Data;
using BioMetrixDatabase.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BioMetrixDatabase.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PatientsController : ControllerBase
    {
        private readonly BioMetrixDbContext _db;

        public PatientsController(BioMetrixDbContext db)
        {
            _db = db;
        }

        /// <summary>Returns all patients with their associated images.</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var patients = await _db.Patients
                .Include(p => p.Images)
                .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
                .ToListAsync();
            return Ok(patients);
        }

        /// <summary>Returns a single patient by ID, including images.</summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(string id)
        {
            var patient = await _db.Patients
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            return patient == null ? NotFound() : Ok(patient);
        }

        /// <summary>
        /// Creates or updates a patient record and replaces its image list.
        /// Called by the desktop app whenever a patient is saved.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> Upsert(string id, [FromBody] PatientEntity incoming)
        {
            incoming.PatientId = id;

            var existing = await _db.Patients
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.PatientId == id);

            if (existing == null)
            {
                // New patient — ensure all image FKs are set
                foreach (var img in incoming.Images)
                    img.PatientId = id;

                await _db.Patients.AddAsync(incoming);
            }
            else
            {
                // Update scalar fields
                _db.Entry(existing).CurrentValues.SetValues(incoming);

                // Replace image list: remove old, add new
                _db.PatientImages.RemoveRange(existing.Images);
                foreach (var img in incoming.Images)
                {
                    img.PatientId = id;
                    await _db.PatientImages.AddAsync(img);
                }
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        /// <summary>Deletes a patient and all associated image records (cascade).</summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var patient = await _db.Patients.FindAsync(id);
            if (patient == null)
                return NotFound();

            _db.Patients.Remove(patient);
            await _db.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Simple health check to confirm the API and database are reachable.</summary>
        [HttpGet("health")]
        public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
    }
}
