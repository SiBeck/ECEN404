namespace MedicalImagingAPI.Database.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string ProviderId { get; set; }
        public string PasswordHash { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public UserRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiry { get; set; }
    }

    public enum UserRole
    {
        Admin = 1,
        Physician = 2,
        Nurse = 3,
        Technician = 4,
        Patient = 5
    }
}