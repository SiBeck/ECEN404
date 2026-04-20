using Microsoft.AspNetCore.Mvc;
using MedicalImagingAPI.Services;
using MedicalImagingAPI.Models;

namespace MedicalImagingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IJwtService _jwtService;
        private readonly IUserAuthenticationService _authService;

        public AuthController(IJwtService jwtService, IUserAuthenticationService authService)
        {
            _jwtService = jwtService;
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _authService.ValidateCredentialsAsync(request.ProviderId, request.Password);

            if (user == null)
                return Unauthorized(new { message = "Invalid credentials" });

            if (!user.IsActive)
                return Unauthorized(new { message = "Account is disabled" });

            var token = _jwtService.GenerateToken(user.Id.ToString(), user.ProviderId, user.Role.ToString());
            var refreshToken = _jwtService.GenerateRefreshToken();

            await _authService.SaveRefreshTokenAsync(user.Id, refreshToken);

            return Ok(new
            {
                accessToken = token,
                refreshToken = refreshToken,
                expiresIn = 3600,
                user = new
                {
                    id = user.Id,
                    providerId = user.ProviderId,
                    name = $"{user.FirstName} {user.LastName}",
                    role = user.Role.ToString()
                }
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            var principal = _jwtService.ValidateToken(request.AccessToken);
            if (principal == null)
                return Unauthorized();

            var userId = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var user = await _authService.ValidateRefreshTokenAsync(Guid.Parse(userId), request.RefreshToken);

            if (user == null)
                return Unauthorized();

            var newToken = _jwtService.GenerateToken(user.Id.ToString(), user.ProviderId, user.Role.ToString());
            var newRefreshToken = _jwtService.GenerateRefreshToken();

            await _authService.SaveRefreshTokenAsync(user.Id, newRefreshToken);

            return Ok(new
            {
                accessToken = newToken,
                refreshToken = newRefreshToken,
                expiresIn = 3600
            });
        }
    }
}
public class LoginRequest
    {
        public string ProviderId { get; set; }
        public string Password { get; set; }
    }

    public class RefreshTokenRequest
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }