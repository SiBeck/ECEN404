using MedicalImagingAPI.Database;
using MedicalImagingAPI.Database.Entities;
using System.Text.Json;

namespace MedicalImagingAPI.Services
{
    public interface IAuditService
    {
        Task LogAsync(Guid userId, string action, string resourceType, string resourceId,
                      bool isSuccess, string? errorMessage = null, object? additionalData = null);
    }

    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(Guid userId, string action, string resourceType, string resourceId,
                                   bool isSuccess, string? errorMessage = null, object? additionalData = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            var auditLog = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                ResourceType = resourceType,
                ResourceId = resourceId,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString() ?? string.Empty,
                Timestamp = DateTime.UtcNow,
                IsSuccess = isSuccess,
                ErrorMessage = errorMessage ?? string.Empty,
                AdditionalData = additionalData != null ? JsonSerializer.Serialize(additionalData) : string.Empty
            };

            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }
    }
}