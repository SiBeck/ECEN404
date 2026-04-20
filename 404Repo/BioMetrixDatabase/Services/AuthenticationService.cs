using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedicalImagingAPI.Services
{
    public class UserAuthenticationService : IUserAuthenticationService
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher _passwordHasher;
        private readonly IConfiguration _configuration;

        public UserAuthenticationService(
            ApplicationDbContext context,
            PasswordHasher passwordHasher,
            IConfiguration configuration)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _configuration = configuration;
        }

        public async Task<User> ValidateCredentialsAsync(string providerId, string password)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ProviderId == providerId && u.IsActive);

            if (user == null)
                return null;

            if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
                return null;

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task SaveRefreshTokenAsync(Guid userId, string refreshToken)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.RefreshToken = refreshToken;

                var refreshTokenExpDays = _configuration
                    .GetSection("JwtSettings:RefreshTokenExpirationDays")
                    .Get<int>();

                user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(refreshTokenExpDays);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<User> ValidateRefreshTokenAsync(Guid userId, string refreshToken)
        {
            var user = await _context.Users.FindAsync(userId);

            if (user == null ||
                user.RefreshToken != refreshToken ||
                user.RefreshTokenExpiry < DateTime.UtcNow)
            {
                return null;
            }

            return user;
        }
    }
}