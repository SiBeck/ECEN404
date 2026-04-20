using MedicalImagingAPI.Database.Entities;
using MedicalImagingAPI.Models;

namespace MedicalImagingAPI.Services
{
    public interface IAuthService
    {
        Task<User?> ValidateCredentialsAsync(string providerId, string password);
        Task SaveRefreshTokenAsync(Guid userId, string refreshToken);
        Task<User?> ValidateRefreshTokenAsync(Guid userId, string refreshToken);
    }
}
