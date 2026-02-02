namespace _403DesktopApp.Models
{
    public class MedicalProvider
    {
        public string ProviderId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Specialty { get; set; }
        public string PasswordHash { get; set; }
        public bool IsActive { get; set; }

        public string FullName => $"{FirstName} {LastName}";
    }
}