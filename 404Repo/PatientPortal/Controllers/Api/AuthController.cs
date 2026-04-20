using Microsoft.AspNetCore.Mvc;
using PatientPortal.Models;
using PatientPortal.Services;

namespace PatientPortal.Controllers.Api
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IPatientAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IPatientAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ProviderId))
                return BadRequest(new { error = "ProviderId is required" });

            // Authenticate by MRN (ProviderId) and optional password.
            var patient = await _authService.AuthenticateAsync(request.ProviderId, request.Password ?? string.Empty);

            if (patient == null)
            {
                _logger.LogWarning("Failed login attempt for ProviderId {ProviderId}", request.ProviderId);
                return Unauthorized(new { error = "Invalid ProviderId or password" });
            }

            _logger.LogInformation("Successful login for Patient {PatientId}", patient.PatientId);

            // Construct response matching the client-side DTO shape.
            var response = new LoginResponseDto
            {
                AccessToken = string.Empty,   // Not issuing real tokens here; desktop client primarily uses success response.
                RefreshToken = string.Empty,
                ExpiresIn = 3600,
                User = new UserInfoDto
                {
                    Id = Guid.NewGuid(), // best-effort: patient id here is int; return a GUID for the client DTO
                    ProviderId = patient.PatientId.ToString(),
                    Name = patient.FullName,
                    Role = "Patient"
                }
            };

            return Ok(response);
        }

        // Server-side DTOs used only for this controller's public API.
        public class LoginRequestDto
        {
            public string ProviderId { get; set; } = string.Empty;
            public string? Password { get; set; }
        }

        public class LoginResponseDto
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public int ExpiresIn { get; set; }
            public UserInfoDto User { get; set; } = new UserInfoDto();
        }

        public class UserInfoDto
        {
            public Guid Id { get; set; }
            public string ProviderId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }
    }
}