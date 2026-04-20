using MedicalImagingAPI.Database.Entities;

namespace MedicalImagingAPI.Services
{
    public interface IUserAuthenticationService
    {
        Task<User> ValidateCredentialsAsync(string providerId, string password);
        Task SaveRefreshTokenAsync(Guid userId, string refreshToken);
        Task<User> ValidateRefreshTokenAsync(Guid userId, string refreshToken);
    }
}