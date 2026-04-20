using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatientPortal.Models;
using PatientPortal.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PatientPortal.Controllers
{
    public class AccountController : Controller
    {
        private readonly IPatientAuthService _authService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(IPatientAuthService authService, ILogger<AccountController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Dashboard", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var patient = await _authService.AuthenticateAsync(model.PatientId, model.Password);

            if (patient == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid Patient ID or password.");
                return View(model);
            }

            // Create claims
            var claims = new List<Claim>
            {
                // Patient model uses PatientId (int) as the primary identifier in this project.
                new Claim(ClaimTypes.NameIdentifier, patient.PatientId.ToString()),
                new Claim(ClaimTypes.Name, patient.FullName),
                new Claim("PatientId", patient.PatientId.ToString()),
                new Claim(ClaimTypes.Email, patient.Email ?? "")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            _logger.LogInformation($"Patient {patient.PatientId} logged in successfully");

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Dashboard", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Dashboard", "Home");
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _authService.RegisterAsync(model);

            if (!result.Success)
            {
                // Surface a helpful, specific message instead of a single generic message.
                ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Registration failed.");
                return View(model);
            }

            _logger.LogInformation($"New patient registered: {result.Patient?.PatientId}");
            TempData["SuccessMessage"] = "Registration successful! Please log in.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("Patient logged out");
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // In this project PatientId is an integer stored in the NameIdentifier claim.
            var patientIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(patientIdClaim) || !int.TryParse(patientIdClaim, out var patientId))
            {
                return RedirectToAction(nameof(Login));
            }

            var success = await _authService.ChangePasswordAsync(patientId, model.CurrentPassword, model.NewPassword);

            if (!success)
            {
                ModelState.AddModelError(string.Empty, "Current password is incorrect.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Password changed successfully!";
            return RedirectToAction("Dashboard", "Home");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
